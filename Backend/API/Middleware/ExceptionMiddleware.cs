using System.Net;
using FluentValidation;

namespace API.Middleware;

public class ExceptionMiddleware(
    RequestDelegate _next, 
    ILogger<ExceptionMiddleware> _logger
)
{
    #region Private Constants
    private const string CONTENT_TYPE = "application/json";
    private const string TASK_CANCELED = "A task was canceled.";
    private const string TIMEOUT_EXCEPTION = "A timeout occurred.";
    private const string OPERATION_CANCELED = "An operation was canceled.";
    private const string HTTP_EXCEPTION = "An HTTP request exception occurred.";
    private const string UNHANDLED_EXCEPTION = "An unhandled exception occurred.";
    private const string VALIDATION_EXCEPTION = "A validation exception occurred.";
    #endregion

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (ValidationException validationException)
        {
            _logger.LogError(validationException, VALIDATION_EXCEPTION);
            await HandleExceptionAsync(context, validationException, HttpStatusCode.BadRequest);
        }
        catch (TaskCanceledException taskCanceledException)
        {
            _logger.LogError(taskCanceledException, TASK_CANCELED);
            await HandleExceptionAsync(context, taskCanceledException, HttpStatusCode.RequestTimeout);
        }
        catch (TimeoutException timeoutException)
        {
            _logger.LogError(timeoutException, TIMEOUT_EXCEPTION);
            await HandleExceptionAsync(context, timeoutException, HttpStatusCode.RequestTimeout);
        }
        catch (HttpRequestException httpRequestException)
        {
            _logger.LogError(httpRequestException, HTTP_EXCEPTION);
            var statusCode = httpRequestException.StatusCode ?? HttpStatusCode.ServiceUnavailable;
            await HandleExceptionAsync(context, httpRequestException, statusCode);
        }
        catch (OperationCanceledException operationCanceledException)
        {
            _logger.LogError(operationCanceledException, OPERATION_CANCELED);
            await HandleExceptionAsync(context, operationCanceledException, HttpStatusCode.RequestTimeout);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, UNHANDLED_EXCEPTION);
            await HandleExceptionAsync(context, ex);
        }
    }

    private static Task HandleExceptionAsync(
        HttpContext context, 
        Exception exception, 
        HttpStatusCode statusCode = HttpStatusCode.InternalServerError
    )
    {
        context.Response.ContentType = CONTENT_TYPE;
        context.Response.StatusCode = (int)statusCode;
        return context.Response.WriteAsJsonAsync(new
        {
            exception.Message,
            context.Response.StatusCode
        });
    }
}