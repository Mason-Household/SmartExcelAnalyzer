using MediatR;
using API.DTOs;
using API.Attributes;
using Persistence.Hubs;
using Application.Queries;
using Application.Commands;
using Domain.Persistence.DTOs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

namespace API.Controllers;

/// <summary>
/// AnalysisController handles the API requests for the LLM and the vector database.
/// dependencies: IMediator, IProgressHubWrapper
/// routes: api/analysis
/// endpoints: /query, /upload
/// </summary>
[ApiController]
[Route("api/[controller]")]
public class AnalysisController(
    IMediator _mediator, 
    IProgressHubWrapper _hubContext, 
    IMemoryCache _cache
) : ControllerBase
{
    /// <summary>
    /// Submits a query to the LLM and returns the answer.
    /// Computes the embedding of the query with the LLM and compares it to the embeddings of the rows in the database.
    /// </summary>
    /// <param name="queryAboutExcelDocument"></param>
    /// <returns>
    ///     string Answer
    ///     string Question
    ///     string DocumentId
    ///     List<Dictionary<string, object>> RelevantRows
    /// </returns>
    [HttpPost("query")]
    [CommonResponseTypesAttribute]
    [ProducesResponseType(typeof(QueryAnswer), StatusCodes.Status200OK)]
    public async Task<IActionResult> SubmitQuery(
        [FromBody] SubmitQuery queryAboutExcelDocument,
        CancellationToken cancellationToken = default
    ) =>
        Ok(await _mediator.Send(queryAboutExcelDocument, cancellationToken));

    /// <summary>
    /// Uploads an excel file to the vector database and returns the documentId. 
    /// If the upload fails, returns null.
    /// Iterates over the rows of the excel file and saves them to the vector database.
    /// Computes each row's embedding with the LLM and stores it in the database.
    /// So when a query is submitted, the LLM can be used to compute the embedding of the query and compare it to the embeddings of the rows.
    /// </summary>
    /// <param name="fileToUpload"></param>
    /// <returns>Document Id - Nullable</returns>
    [HttpPost("upload")]
    [CommonResponseTypesAttribute]
    [ProducesResponseType(typeof(BaseResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> UploadFile(
        [FromForm] IFormFile fileToUpload,
        CancellationToken cancellationToken = default
    ) 
    {
        var uploadId = fileToUpload.FileName + DateTime.UtcNow.ToString()[8..];
        _cache.Set(fileToUpload.FileName, uploadId);
        return Ok(
            new
            {
                Filename = fileToUpload.FileName,
                DocumentId = await _mediator.Send(new UploadFileCommand
                {
                    File = fileToUpload,
                    Progress = new Progress<(
                        double ParseProgress,
                        double SaveProgress
                    )>(
                        async progressTuple =>
                            await _hubContext.SendProgress(
                                progressTuple.ParseProgress,
                                progressTuple.SaveProgress,
                                cancellationToken
                            )
                    )
                }, cancellationToken)
            }
        );
    }
        

    [HttpPost("initialize-upload")]
    [CommonResponseTypesAttribute]
    [ProducesResponseType(typeof(InitializeUploadResponse), StatusCodes.Status200OK)]
    public IActionResult InitializeUpload([FromBody] InitializeUploadRequest request)
    {
        var uploadId = request.Filename + DateTime.UtcNow.ToString()[8..];
        _cache.Set(request.Filename, uploadId);
        _cache.Set(uploadId, request.Filename);
        return Ok(new InitializeUploadResponse
        {
            Filename = request.Filename,
            DocumentId = uploadId
        });
    }

    [HttpPost("upload-chunk")]
    [CommonResponseTypesAttribute]
    [ProducesResponseType(typeof(UploadResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> UploadChunk(
        [FromForm] IFormFile file,
        [FromForm] int chunkIndex,
        [FromForm] int totalChunks,
        CancellationToken cancellationToken = default
    )
    {
        var uploadCommand = new UploadFileCommand
        {
            File = file,
            Progress = new Progress<(
                double ParseProgress,
                double SaveProgress
            )>(
                async progressTuple =>
                    await _hubContext.SendProgress(
                        progressTuple.ParseProgress,
                        progressTuple.SaveProgress,
                        cancellationToken
                    )
            )
        };
        var uploadId = _cache.Get<string>(file.FileName);
        if (uploadId is null) 
            return BadRequest("Upload not found");

        await _mediator.Send(uploadCommand, cancellationToken);
        double progress = (double)(chunkIndex + 1) / totalChunks * 100;
        await _hubContext.SendProgress(progress, totalChunks, cancellationToken);

        return Ok(new UploadResponse
        {
            Filename = file.FileName,
            DocumentId = uploadId!,
            ChunkIndex = chunkIndex,
            ChunkCount = totalChunks,
            ChunkSize = (int)file.Length,
            ChunkOffset = chunkIndex * (int)file.Length,
            ChunkLength = (int)file.Length,
        });
    }

    [HttpPost("finalize-upload")]
    [CommonResponseTypesAttribute]
    [ProducesResponseType(typeof(FinalizeUploadResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> FinalizeUpload([FromBody] FinalizeUploadRequest request, CancellationToken cancellationToken = default)
    {
        var fileName = _cache.Get<string>(request.UploadId);
        await _hubContext.SendProgress(100, 100, cancellationToken);
        _cache.Remove(request.UploadId);
        if (fileName is not null && fileName is { Length: > 0 }) _cache.Remove(fileName);
        return Ok(new FinalizeUploadResponse
        {
            Filename = fileName,
            DocumentId = request.UploadId
        });
    }
}
