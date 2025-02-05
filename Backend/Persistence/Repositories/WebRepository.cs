using System.Text;
using Newtonsoft.Json;

namespace Persistence.Repositories;

public interface IWebRepository<T>
{
    Task<T> PostAsync(
        string endpoint,
        object payload,
        CancellationToken cancellationToken = default
    );
}

public class WebRepository<T>(
    IHttpClientFactory httpClientFactory
) : IWebRepository<T>
{
    private const string ContentType = "application/json";
    private const string DefaultClientName = "DefaultClient";
    /// <summary>
    /// PostAsync sends a POST request to the specified endpoint with the given payload.
    /// The payload is serialized to the given type T.
    /// </summary>
    /// <param name="endpoint"></param>
    /// <param name="payload"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public async Task<T> PostAsync(
        string endpoint,
        object payload,
        CancellationToken cancellationToken = default
    )
    {
        var content = new StringContent(
            JsonConvert.SerializeObject(payload), 
            Encoding.UTF8, 
            ContentType
        );
        var response = await httpClientFactory
            .CreateClient(DefaultClientName)
            .PostAsync(
                endpoint, 
                content, 
                cancellationToken
            );
        response.EnsureSuccessStatusCode();
        return JsonConvert.DeserializeObject<T>(
            await response
                .Content
                .ReadAsStringAsync(cancellationToken)
        )!;
    }
}