using System.Text.Json;
using System.Diagnostics;
using Domain.Extensions;
using Persistence.Database;
using Domain.Persistence.DTOs;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using Domain.Persistence.Configuration;
using Microsoft.Extensions.Caching.Memory;

namespace Persistence.Repositories;

public interface IVectorDbRepository
{
    Task<string?> SaveDocumentAsync(
        SummarizedExcelData vectorSpreadsheetData, 
        IProgress<(double ParseProgress, double SaveProgress)>? progress = null,
        CancellationToken cancellationToken = default
    );

    Task<SummarizedExcelData> QueryVectorDataAsync(
        string documentId, 
        float[] queryVector, 
        int topRelevantCount = 10, 
        CancellationToken cancellationToken = default
    );
}

/// <summary>
/// Repository for interacting with the VectorDb database.
/// This repository is responsible for saving and querying documents from the VectorDb database.
/// </summary>
/// <param name="databaseWrapper"></param>
/// <param name="logger"></param>
/// <param name="llmRepository"></param>
/// <param name="llmOptions"></param>
/// <param name="databaseOptions"></param>
public class VectorRepository(
    IDatabaseWrapper _database,
    ILogger<VectorRepository> _logger,
    ILLMRepository _llmRepository,
    IOptions<LLMServiceOptions> llmOptions,
    IOptions<DatabaseOptions> databaseOptions,
    IMemoryCache _cache
) : IVectorDbRepository
{
    #region Logging Message Constants
    private const string LOG_NULL_INPUT_DATA = "Input data is null.";
    private const string LOG_NULL_DOCUMENT_ID = "Document ID is null or empty.";
    private const string LOG_EMPTY_QUERY_VECTOR = "Query vector is empty or null.";
    private const string LOG_NULL_EMBEDDING = "Embedding at index {Index} is null.";    
    private const string LOG_START_SAVE = "Starting to save document to the database.";
    private const string LOG_FAIL_SAVE_BATCH = "Failed to save vectors to the database.";
    private const string LOG_SUCCESS_SAVE = "Saved document with id {DocumentId} to the database.";
    private const string LOG_START_COMPUTE = "Computing embeddings for document with {Count} rows.";
    private const string LOG_FAIL_SAVE_VECTORS = "Failed to save vectors of the document to the database.";
    private const string LOG_COMPUTE_EMBEDDINGS = "Computed {Count} embeddings in {ElapsedMilliseconds}ms";
    private const string LOG_START_QUERY = "Querying the VectorDb for the most relevant rows for document {DocumentId}.";
    private const string LOG_FAIL_SAVE_SUMMARY = "Failed to save the summary of the document with Id {Id} to the database.";
    private const string LOG_FAIL_QUERY_SUMMARY = "Failed to query the summary of the document with Id {Id} from the database.";
    private const string LOG_FAIL_SAVE_BATCH_FOR_DOCUMENT = "Failed to save vectors to the database for document {DocumentId}.";
    private const string LOG_FAIL_QUERY_ROWS = "Failed to query the relevant rows of the document with Id {Id} from the database.";
    private const string LOG_INCONSISTENT_IDS = "Inconsistent document IDs across batches. Document Id {DocumentId} is not equal to batch document Id {BatchDocumentId}.";
    private const string LOG_SUCCESS_QUERY = "Querying the VectorDb for the most relevant rows for document {DocumentId} was successful. Found {RelevantRowsCount} relevant rows.";
    #endregion

    #region Dependencies
    private readonly int _computeEmbeddingBatchSize = llmOptions.Value.COMPUTE_BATCH_SIZE;
    private readonly int _maxConcurrentTasks = databaseOptions.Value.MAX_CONNECTION_COUNT;
    #endregion

    #region Public Methods
    /// <summary>
    /// Saves the document to the database.
    /// Computes the embeddings of the rows in batches and stores them in the database.
    /// </summary>
    /// <param name="vectorSpreadsheetData"></param>
    /// <param name="progress"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public async Task<string?> SaveDocumentAsync(
        SummarizedExcelData? vectorSpreadsheetData = null, 
        IProgress<(double ParseProgress, double SaveProgress)>? progress = null, 
        CancellationToken cancellationToken = default
    )
    {
        if (vectorSpreadsheetData is null)
        {
            _logger.LogWarning(LOG_NULL_INPUT_DATA);
            return null!;
        }
        _logger.LogInformation(LOG_START_SAVE);
        var documentId = await SaveDocumentDataAsync(
            vectorSpreadsheetData.FileName,
            vectorSpreadsheetData.Rows ?? [], 
            progress, 
            cancellationToken
        );
        if (string.IsNullOrWhiteSpace(documentId))
        {
            _logger.LogWarning(LOG_FAIL_SAVE_VECTORS);
            return null!;
        }
        if (vectorSpreadsheetData.Summary is not null)
        {
            var summarySuccess = await _database.StoreSummaryAsync(
                documentId, 
                vectorSpreadsheetData.Summary, 
                cancellationToken
            );
            if (summarySuccess is < 0) _logger.LogWarning(LOG_FAIL_SAVE_SUMMARY, documentId);
        }
        _logger.LogInformation(LOG_SUCCESS_SAVE, documentId);
        return documentId;
    }

    /// <summary>
    /// Queries the VectorDb for the most relevant rows for a given document and query vector.
    /// Compares the query vector to the embeddings of the rows in the database and returns the most relevant rows.
    /// </summary>
    /// <param name="documentId"></param>
    /// <param name="queryVector"></param>
    /// <param name="topRelevantCount"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public async Task<SummarizedExcelData> QueryVectorDataAsync(
        string documentId, 
        float[] queryVector, 
        int topRelevantCount = 10, 
        CancellationToken cancellationToken = default
    )
    {
        if (string.IsNullOrWhiteSpace(documentId))
        {
            _logger.LogWarning(LOG_NULL_DOCUMENT_ID);
            return null!;
        }
        if (queryVector is null or { Length: 0 })
        {
            _logger.LogWarning(LOG_EMPTY_QUERY_VECTOR);
            return null!;
        }
        _logger.LogInformation(LOG_START_QUERY, documentId);
        var relevantDocuments = await _database.GetRelevantDocumentsAsync(
            documentId, 
            queryVector, 
            topRelevantCount, 
            cancellationToken
        );
        if (relevantDocuments is null || !relevantDocuments.Any())
        {
            _logger.LogWarning(LOG_FAIL_QUERY_ROWS, documentId);
            return null!;
        }
        var rows = new ConcurrentBag<ConcurrentDictionary<string, object>>(relevantDocuments);
        var summary = await _database.GetSummaryAsync(documentId, cancellationToken);
        if (summary is null || summary.IsEmpty)
        {
            _logger.LogWarning(LOG_FAIL_QUERY_SUMMARY, documentId);
            return new() 
            { 
                Rows = rows,
                Summary = null!
            };
        }
        _logger.LogInformation(LOG_SUCCESS_QUERY, documentId, relevantDocuments.Count());
        return new()
        {
            Rows = rows,
            Summary = summary
        };
    }
    #endregion

    #region Private Data Processing Methods
    /// <summary>
    /// Processes the rows in batches and computes the embeddings for each batch.
    /// Splits up the task into the producer/consumer model to reduce the time taken to compute the embeddings and store them in the db
    /// </summary>
    /// <param name="rows"></param>
    /// <param name="progress"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    private async Task<string> SaveDocumentDataAsync(
        string fileName,
        ConcurrentBag<ConcurrentDictionary<string, object>> rows, 
        IProgress<(double, double)>? progress, 
        CancellationToken cancellationToken = default
    )
    {
        var batchChannel = Channel.CreateBounded<IEnumerable<ConcurrentDictionary<string, object>>>(new BoundedChannelOptions(Math.Max(1,Environment.ProcessorCount - 4))
        {
            FullMode = BoundedChannelFullMode.Wait
        });
        var embeddingChannel = Channel.CreateBounded<(IEnumerable<float[]> Embeddings, IEnumerable<ConcurrentDictionary<string, object>> Batch)>(new BoundedChannelOptions(Math.Max(1, Environment.ProcessorCount - 4))
        {
            FullMode = BoundedChannelFullMode.Wait
        });
        var totalRows = rows.Count;
        var createBatchesTask = CreateBatchesAsync(
            rows, 
            batchChannel.Writer, 
            progress, 
            totalRows, 
            cancellationToken
        );
        var computeEmbeddingsTask = ComputeEmbeddingsAsync(
            batchChannel.Reader, 
            embeddingChannel.Writer, 
            progress, 
            totalRows, 
            cancellationToken
        );
        var storeEmbeddingsTask = StoreEmbeddingsAsync(
            embeddingChannel.Reader, 
            progress,
            totalRows,
            fileName,
            cancellationToken
        );
        await createBatchesTask;
        await computeEmbeddingsTask;
        return await storeEmbeddingsTask;
    }

    private async Task CreateBatchesAsync(
        ConcurrentBag<ConcurrentDictionary<string, object>> rows,
        ChannelWriter<IEnumerable<ConcurrentDictionary<string, object>>> writer,
        IProgress<(double, double)>? progress,
        int totalRows,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            var processedRows = 0;
            cancellationToken.ThrowIfCancellationRequested();
            var batch = new ConcurrentBag<ConcurrentDictionary<string, object>>();
            await rows.ForEachAsync(
                cancellationToken, 
                async (row, ct) =>
                {
                    ct.ThrowIfCancellationRequested();
                    batch.Add(row);
                    Interlocked.Increment(ref processedRows);
                    progress?.Report((processedRows / (double)totalRows, 0));
                    if (batch.Count.Equals(_computeEmbeddingBatchSize))
                    {
                        var batchToWrite = new ConcurrentBag<ConcurrentDictionary<string, object>>(batch);
                        batch.Clear();
                        await writer.WriteAsync(batchToWrite, cancellationToken);
                    }
                    await Task.Yield();
                }
            );

            if (!batch.IsEmpty) await writer.WriteAsync(batch, cancellationToken);
        }
        finally
        {
            writer.Complete();
        }
    }

    private async Task ComputeEmbeddingsAsync(
        ChannelReader<IEnumerable<ConcurrentDictionary<string, object>>> reader,
        ChannelWriter<(IEnumerable<float[]>, IEnumerable<ConcurrentDictionary<string, object>>)> writer,
        IProgress<(double, double)>? progress,
        int totalRows,
        CancellationToken cancellationToken = default
    )
    {
        var processedRows = 0;
        var serializerOptions =  new JsonSerializerOptions { WriteIndented = false };
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await foreach (var batch in reader.ReadAllAsync(cancellationToken))
            {
                _logger.LogInformation(LOG_START_COMPUTE, batch.Count());
                var stopwatch = Now;
                var embeddings = await _llmRepository.ComputeBatchEmbeddings(
                    batch.Select(
                        row => JsonSerializer.Serialize(row, serializerOptions)
                    ),
                    cancellationToken
                );
                stopwatch.Stop();
                _logger.LogInformation(LOG_COMPUTE_EMBEDDINGS, batch.Count(), stopwatch.ElapsedMilliseconds);
                if (embeddings is not null && embeddings.Any())
                {
                    await writer.WriteAsync((embeddings, batch)!, cancellationToken);
                    processedRows += batch.Count();
                    progress?.Report((1, processedRows / (double)totalRows));
                }
                else _logger.LogWarning(LOG_FAIL_SAVE_BATCH);
            }
        }
        finally
        {
            writer.Complete();
        }
    }

    private async Task<string> StoreEmbeddingsAsync(
        ChannelReader<(
            IEnumerable<float[]> Embeddings, 
            IEnumerable<ConcurrentDictionary<string, object>> Batch
        )> reader,
        IProgress<(double ParseProgress, double SaveProgress)>? progress,
        int totalRows,
        string fileName,
        CancellationToken cancellationToken = default
    )
    {
        var processedRows = 0;
        string? documentId = _cache.Get<string?>(fileName);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await foreach (var message in reader.ReadAllAsync(cancellationToken))
            {
                documentId = await ProcessChannelMessageAsync(
                    message, 
                    progress, 
                    totalRows, 
                    processedRows, 
                    documentId!, 
                    cancellationToken
                );
            }
        }
        finally
        {
            progress?.Report((1, 1));
        }
        return documentId ?? string.Empty;
    }

    private async Task<string?> ProcessChannelMessageAsync(
        (   
            IEnumerable<float[]> Embeddings, 
            IEnumerable<ConcurrentDictionary<string, object>> Batch
        ) message, 
        IProgress<(double ParseProgress, double SaveProgress)>? progress, 
        int totalRows, 
        int processedRows, 
        string documentId, 
        CancellationToken cancellationToken = default
    )
    {
        var semaphore = new SemaphoreSlim(_maxConcurrentTasks);
        await semaphore.WaitAsync(cancellationToken);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var batchesToStore = new ConcurrentBag<ConcurrentDictionary<string, object>>();
            await ProcessBatchAsync(
                message.Embeddings, 
                message.Batch, 
                batchesToStore, 
                cancellationToken
            );
            documentId = await SaveBatchOfRowEmbeddings(
                documentId, 
                batchesToStore, 
                cancellationToken
            );
            processedRows += message.Batch.Count();
            progress?.Report((1, processedRows / (double)totalRows));
            return documentId;
        }
        finally
        {
            semaphore.Release();
        }
    }

    private async Task ProcessBatchAsync(
        IEnumerable<float[]> embeddings,
        IEnumerable<ConcurrentDictionary<string, object>> batch,
        ConcurrentBag<ConcurrentDictionary<string, object>> batchesToStore,
        CancellationToken cancellationToken = default
    )
    {
        var parallelOptions = new ParallelOptions 
        { 
            MaxDegreeOfParallelism = Math.Max(-1, _maxConcurrentTasks),
            CancellationToken = cancellationToken
        };
        var pairs = batch.Zip(embeddings, (row, embedding) => (row, embedding));
        await pairs.ForEachAsync(
            cancellationToken, 
            async (pair, ct) =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var (row, embedding) = pair;
                if (embedding is not null) row["embedding"] = embedding;
                else _logger.LogWarning(LOG_NULL_EMBEDDING, "Unknown");

                batchesToStore.Add(row);
                await Task.Yield();
            }
        );
    }

    private async ValueTask<string> SaveBatchOfRowEmbeddings(
        string documentId,
        ConcurrentBag<ConcurrentDictionary<string, object>> batchOfRowsToSave, 
        CancellationToken cancellationToken = default
    )
    {
        var batchDocumentId = await _database.StoreVectorsAsync(
            batchOfRowsToSave, 
            documentId, 
            cancellationToken
        );
        if (batchDocumentId is null)
        {
            _logger.LogWarning(LOG_FAIL_SAVE_BATCH_FOR_DOCUMENT, documentId);
            return documentId;
        }
        if (documentId is null) documentId = batchDocumentId;
        else if (documentId != batchDocumentId) _logger.LogWarning(LOG_INCONSISTENT_IDS, documentId, batchDocumentId);
        return documentId;
    }

    private static Stopwatch Now => Stopwatch.StartNew();
    #endregion
}
