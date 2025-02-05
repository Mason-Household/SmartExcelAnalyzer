using MongoDB.Bson;
using MongoDB.Driver;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Diagnostics.CodeAnalysis;
using Domain.Persistence.Configuration;

namespace Persistence.Database;

/// <summary>
/// MongoDatabaseWrapper wraps the MongoDB database and provides methods to interact with it.
/// Implements IDatabaseWrapper
/// Dependencies: IMongoDatabase, IOptions<DatabaseOptions>, ILogger<MongoDatabaseWrapper>
/// Options used: MAX_RETRY_COUNT, SAVE_BATCH_SIZE, MAX_CONNECTION_COUNT
/// </summary>
/// <param name="database"></param>
/// <param name="options"></param>
/// <param name="logger"></param>
[Obsolete("This class is not used in the current implementation. It is kept for reference purposes.")]
[ExcludeFromCodeCoverage]
public class MongoDatabaseWrapper(
    IMongoDatabase _database,
    IOptions<DatabaseOptions> _options,
    ILogger<MongoDatabaseWrapper> _logger
) : IDatabaseWrapper
{
    #region Database Options
    private int MaxRetries => _options.Value.MAX_RETRY_COUNT;
    private int BatchSize => _options.Value.SAVE_BATCH_SIZE;
    private string DocumentsCollection => _options.Value.CollectionName;
    private string SummariesCollection => _options.Value.CollectionNameTwo;
    #endregion
    private readonly JsonSerializerOptions _serializerOptions = new() { PropertyNameCaseInsensitive = true };

    /// <summary>
    /// StoreVectorsAsync stores the vectors in the database in batches.
    /// </summary>
    /// <param name="rows"></param>
    /// <param name="docId"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public async Task<string?> StoreVectorsAsync(
        IEnumerable<ConcurrentDictionary<string, object>> rows,
        string? docId = default,
        CancellationToken cancellationToken = default
    )
    {
        var collection = _database.GetCollection<BsonDocument>("documents");
        var documentId = docId ?? ObjectId.GenerateNewId().ToString();
        var batchChannel = Channel.CreateBounded<IEnumerable<ConcurrentDictionary<string, object>>>(new BoundedChannelOptions(20) { FullMode = BoundedChannelFullMode.Wait });
        var producerTask = ProduceBatchesAsync(rows, batchChannel.Writer, cancellationToken);
        var consumerTask = ConsumeAndStoreBatchesAsync(batchChannel.Reader, collection, documentId, cancellationToken);
        await Task.WhenAll(producerTask, consumerTask);
        return documentId;
    }

    private async Task ProduceBatchesAsync(
        IEnumerable<ConcurrentDictionary<string, object>> rows,
        ChannelWriter<IEnumerable<ConcurrentDictionary<string, object>>> writer,
        CancellationToken cancellationToken
    )
    {
        try
        {
            await foreach (var batch in CreateBatchesAsync(rows, cancellationToken)) await writer.WriteAsync(batch, cancellationToken);
        }
        finally
        {
            writer.Complete();
        }
    }

    private async IAsyncEnumerable<IEnumerable<ConcurrentDictionary<string, object>>> CreateBatchesAsync(
        IEnumerable<ConcurrentDictionary<string, object>> rows,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        var batch = new List<ConcurrentDictionary<string, object>>(BatchSize);
        foreach (var row in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            batch.Add(row);
            if (batch.Count == BatchSize)
            {
                yield return batch;
                batch = new List<ConcurrentDictionary<string, object>>(BatchSize);
                await Task.CompletedTask;
            }
        }
        if (batch.Count > 0)
        {
            yield return batch;
        }
        await Task.CompletedTask;
    }

    private async Task ConsumeAndStoreBatchesAsync(
        ChannelReader<IEnumerable<ConcurrentDictionary<string, object>>> reader,
        IMongoCollection<BsonDocument> collection,
        string documentId,
        CancellationToken cancellationToken
    )
    {
        var tasks = new List<Task>();
        var semaphore = new SemaphoreSlim(Math.Max(1,Environment.ProcessorCount-1));
        await foreach (var batch in reader.ReadAllAsync(cancellationToken))
        {
            await semaphore.WaitAsync(cancellationToken);
            tasks.Add(Task.Run(async () =>
            {
                try
                {
                    await ProcessBatchAsync(batch, collection, documentId, cancellationToken);
                }
                finally
                {
                    semaphore.Release();
                }
            }, cancellationToken));
        }
        await Task.WhenAll(tasks);
    }

    private async Task ProcessBatchAsync(
        IEnumerable<ConcurrentDictionary<string, object>> batch,
        IMongoCollection<BsonDocument> collection,
        string documentId,
        CancellationToken cancellationToken
    ) =>
        await UpdateBatchWithRetryAsync(collection, CreateBatchUpdate(batch, documentId), cancellationToken);

    private static (FilterDefinition<BsonDocument> Filter, UpdateDefinition<BsonDocument> Update) CreateBatchUpdate(
        IEnumerable<ConcurrentDictionary<string, object>> batch,
        string documentId
    )
    {
        var filter = Builders<BsonDocument>.Filter.Eq("_id", documentId);
        var updateBuilder = Builders<BsonDocument>.Update;
        var rowsToAdd = batch.Select(row => new BsonDocument
        {
            { "content", BsonDocument.Parse(JsonSerializer.Serialize(row)) },
            { "embedding", new BsonArray(row["embedding"] as float[] ?? []) }
        });
        var update = updateBuilder.PushEach("rows", rowsToAdd);
        return (filter, update);
    }

    private async Task UpdateBatchWithRetryAsync(
        IMongoCollection<BsonDocument> collection,
        (FilterDefinition<BsonDocument> Filter, UpdateDefinition<BsonDocument> Update) batchUpdate,
        CancellationToken cancellationToken
    )
    {
        var retryCount = 0;
        while (retryCount < MaxRetries)
        {
            try
            {
                var options = new BulkWriteOptions { IsOrdered = false };
                var updateModel = new UpdateOneModel<BsonDocument>(batchUpdate.Filter, batchUpdate.Update)
                {
                    IsUpsert = true
                };
                await collection.BulkWriteAsync([updateModel], options, cancellationToken);
                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                await HandleUpsertErrorAsync(ex, retryCount);
                retryCount++;
            }
        }
        throw new Exception($"Failed to update batch after {MaxRetries} attempts.");
    }

    private async Task HandleUpsertErrorAsync(Exception ex, int retryCount)
    {
        _logger.LogWarning(ex, "Failed to insert batch. Retry attempt: {RetryCount}", retryCount + 1);
        await Task.Delay(1000 * (retryCount + 1)); // Exponential backoff
    }

    /// <summary>
    /// StoreSummaryAsync stores the summary of the document in the database.
    /// </summary>
    /// <param name="documentId"></param>
    /// <param name="summary"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public async Task<int?> StoreSummaryAsync(string documentId, ConcurrentDictionary<string, object> summary, CancellationToken cancellationToken = default)
    {
        try
        {
            var collection = _database.GetCollection<BsonDocument>("summaries");
            var document = new BsonDocument
            {
                { "_id", documentId },
                { "content", BsonDocument.Parse(JsonSerializer.Serialize(summary)) }
            };
            await collection.InsertOneAsync(document, cancellationToken: cancellationToken);
            return summary.Count; // MongoDB doesn't tell us how many went in
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save the summary of the document with id {Id} to the database.", documentId);
            return null;
        }
    }

    /// <summary>
    /// GetRelevantDocumentsAsync queries the database for the most relevant documents to the query vector.
    /// Biggest advantage of mongo - dot product aggregation.
    /// </summary>
    /// <param name="documentId"></param>
    /// <param name="queryVector"></param>
    /// <param name="topRelevantCount"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public async Task<IEnumerable<ConcurrentDictionary<string, object>>> GetRelevantDocumentsAsync(string documentId, float[] queryVector, int topRelevantCount, CancellationToken cancellationToken = default)
    {
        var collection = _database.GetCollection<BsonDocument>(DocumentsCollection);
        var pipeline = new BsonDocument[]
        {
            new("$match", new BsonDocument("_id", documentId)),
            new("$unwind", "$rows"),
            new("$addFields",
                new BsonDocument("vectorScore",
                new BsonDocument("$dotProduct",
                new BsonArray { "$rows.embedding", new BsonArray(queryVector) }))
            ),
            new("$sort", new BsonDocument("vectorScore", -1)),
            new("$limit", topRelevantCount),
            new("$replaceRoot", new BsonDocument("newRoot", "$rows.content"))
        };
        var results = await collection.Aggregate<BsonDocument>(pipeline, cancellationToken: cancellationToken).ToListAsync(cancellationToken);
        return results.Select(doc => JsonSerializer.Deserialize<ConcurrentDictionary<string, object>>(doc.ToJson(), _serializerOptions)!);
    }

    /// <summary>
    /// Simple lookup on the summaries collection
    /// </summary>
    /// <param name="documentId"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public async Task<ConcurrentDictionary<string, object>> GetSummaryAsync(string documentId, CancellationToken cancellationToken = default)
    {
        var collection = _database.GetCollection<BsonDocument>(SummariesCollection);
        var filter = Builders<BsonDocument>.Filter.Eq("_id", documentId);
        var result = await collection.Find(filter).FirstOrDefaultAsync(cancellationToken);
        if (result is null) return new ConcurrentDictionary<string, object>();
        return JsonSerializer.Deserialize<ConcurrentDictionary<string, object>>(result["content"].ToJson(), _serializerOptions)!;
    }
}