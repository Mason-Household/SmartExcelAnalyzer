using Qdrant.Client;
using System.Text.Json;
using Domain.Extensions;
using Qdrant.Client.Grpc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Domain.Persistence.Configuration;
using static Qdrant.Client.Grpc.Conditions;

namespace Persistence.Database;

/// <summary>
/// Qdrant Database Wrapper
/// Stores the data in Qdrant Database. 
/// Uses two collections:
/// - Document Collection: Stores the vectors of the excel file
/// - Summary Collection: Stores the summary of the excel file
/// </summary>
/// <param name="_client"></param>
/// <param name="options"></param>
/// <param name="_logger"></param>
public class QdrantDatabaseWrapper(
    IQdrantClient _client,
    IOptions<DatabaseOptions> options,
    ILogger<QdrantDatabaseWrapper> _logger
) : IDatabaseWrapper
{
    #region Fields
    private static byte[] NextRandomBytes
    {
        get
        {
            Random.NextBytes(RandomBytes);
            return RandomBytes;
        }
    }
    private static Random Random => new();
    private static byte[] RandomBytes => new byte[4];
    private int BatchSize => options.Value.SAVE_BATCH_SIZE;
    private readonly Vectors _dummyVector = new(new float[] { 0.0f });
    private string DocumentCollectionName => options.Value.CollectionName;
    private string SummaryCollectionName => options.Value.CollectionNameTwo;
    private int MaxDegreeOfParallelism => options.Value.MAX_CONNECTION_COUNT;
    private readonly JsonSerializerOptions _serializerOptions = new() { PropertyNameCaseInsensitive = true };
    #endregion

    #region Public Methods
    /// <summary>
    /// StoreVectorsAsync stores the vectors in the database.
    /// Creates the rows in parallel and inserts them in batches.
    /// </summary>
    /// <param name="rows"></param>
    /// <param name="docId"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public async Task<string?> StoreVectorsAsync(
        IEnumerable<ConcurrentDictionary<string, object>> rows,
        string? documentId = null,
        CancellationToken cancellationToken = default
    )
    {
        _logger.LogInformation("Starting StoreVectorsAsync with {RowCount} rows", rows.Count());
        documentId ??= NewDocumentId();
        _logger.LogInformation("Document ID: {DocumentId}", documentId);
        var points = await CreateRowsInParallelAsync(documentId, rows, cancellationToken);
        _logger.LogInformation("Created {PointCount} points", points.Count());
        if (points.Any())
        {
            try
            {
                await InsertRowsInBatchesAsync(points, cancellationToken);
                _logger.LogInformation("Successfully inserted all points");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to insert points");
                return null;
            }
        }
        else
        {
            _logger.LogWarning("No points to insert");
        }
        return documentId;
    }

    /// <summary>
    /// StoreSummaryAsync stores the summary of the document in the database.
    /// Dummy vector - Qdrant requires a vector
    /// </summary>
    /// <param name="documentId"></param>
    /// <param name="summary"></param>
    /// <param name="cancellationToken"></param>
    /// <returns>Greater than 0 if success</returns>
    public async Task<int?> StoreSummaryAsync(
        string documentId,
        ConcurrentDictionary<string, object> summary,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            var summaryData = new PointStruct 
            { 
                Id = new PointId(), 
                Vectors = _dummyVector 
            };
            summaryData.Payload["is_summary"] = new Value { BoolValue = true };
            summaryData.Payload["document_id"] = new Value { StringValue = documentId };
            summaryData.Payload["content"] = new Value { StringValue = JsonSerializer.Serialize(summary, _serializerOptions) };
            await _client.UpsertAsync(
                points: [ summaryData ],
                collectionName: SummaryCollectionName,
                cancellationToken: cancellationToken
            );
            return summary.Count;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save the summary of the document with id {Id} to the database.", documentId);
            return null;
        }
    }

    /// <summary>
    /// GetRelevantDocumentsAsync queries the database for the most relevant documents to the query vector.
    /// </summary>
    /// <param name="documentId"></param>
    /// <param name="queryVector"></param>
    /// <param name="topRelevantCount"></param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public async Task<IEnumerable<ConcurrentDictionary<string, object>>> GetRelevantDocumentsAsync(
        string documentId,
        float[] queryVector,
        int topRelevantCount,
        CancellationToken cancellationToken = default
    )
    {
        var searchResult = await _client.SearchAsync(// Native Qdrant Vector Search
            collectionName: DocumentCollectionName,
            vector: queryVector.AsMemory(),// Our Query as a vector interpreted by the LLM
            filter: MatchKeyword("document_id", documentId), // On our specific excel file
            limit: (ulong)topRelevantCount, // Number of results the user is interested in
            cancellationToken: cancellationToken
        );
        return searchResult?
            .Select(point => point.Payload)
            .Select(point => point.TryGetValue("content", out var contentValue)
                ? contentValue.StringValue
                : null)
            .Select(content => content is not null
                ? JsonSerializer.Deserialize<ConcurrentDictionary<string, object>>(content, _serializerOptions)
                : null)
            .Where(content => content is not null)
            .Select(content => content!)
        ?? [];
    }

    /// Simple lookup for the summary
    /// </summary>
    /// <param name="documentId"></param>
    /// <param name="cancellationToken"></param>    
    /// <returns></returns>
    public async Task<ConcurrentDictionary<string, object>> GetSummaryAsync(
        string documentId, 
        CancellationToken cancellationToken = default
    )
    {
        var searchResult = await _client.SearchAsync(
            collectionName: SummaryCollectionName,
            vector: new float[1],
            filter: MatchKeyword("document_id", documentId),
            limit: 1,
            cancellationToken: cancellationToken
        );
        if (searchResult is null or { Count: 0 }) 
            return new();

        if (!searchResult[0]!.Payload.TryGetValue("content", out Value? value)) 
            return new();

        return JsonSerializer.Deserialize<ConcurrentDictionary<string, object>>(value.StringValue, _serializerOptions)!;
    }
    #endregion

    #region Private Methods
    private static string NewDocumentId() => BitConverter.ToString(NextRandomBytes).Replace("-", "");

    private async Task<IEnumerable<PointStruct>> CreateRowsInParallelAsync(
        string? documentId,
        IEnumerable<ConcurrentDictionary<string, object>> rows,
        CancellationToken cancellationToken = default
    )
    {
        var parallelOptions = CreateParallelOptions(cancellationToken);
        var points = new ConcurrentBag<PointStruct>();
        await rows.ForEachAsync(
            cancellationToken,
            async (row, ct) =>
            {
                points.Add(await CreateRow(documentId!, row));
                await Task.Yield();
            }
        );
        return points;
    }

    private async Task<PointStruct> CreateRow(
        string? documentId, 
        ConcurrentDictionary<string, object> row
    )
    {
        var point = new PointStruct
        {
            Id = new PointId(),
            Vectors = row.TryGetValue("embedding", out var embedding)
                ? embedding as Vectors ?? Array.Empty<float>()
                : Array.Empty<float>()
        };
        if (documentId is not null) point.Payload.Add("document_id", new Value { StringValue = documentId.ToString() });
        point.Payload.Add("content", new Value { StringValue = JsonSerializer.Serialize(row, _serializerOptions) });
        await Task.Yield();
        return point;
    }

    private async Task InsertRowsInBatchesAsync(
        IEnumerable<PointStruct> points,
        CancellationToken cancellationToken = default
    )
    {
        int totalInserted = 0;
        foreach (var batch in points.Chunk(BatchSize))
        {
            try
            {
                await _client.UpsertAsync(
                    points: batch,
                    collectionName: DocumentCollectionName,
                    cancellationToken: cancellationToken
                );
                totalInserted += batch.Length;
                _logger.LogInformation("Successfully inserted batch of {BatchSize} vectors. Progress: {Progress}", batch.Length, totalInserted);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to insert batch starting at index {StartIndex}", totalInserted);
                throw;
            }
        }
    }

    private ParallelOptions CreateParallelOptions(CancellationToken cancellationToken = default) =>
        new()
        {
            CancellationToken = cancellationToken,
            MaxDegreeOfParallelism = Math.Max(-1, MaxDegreeOfParallelism)
        };
    #endregion
}

public interface IQdrantClient
{
    Task<UpdateResult> UpsertAsync(
        string collectionName,
        IReadOnlyList<PointStruct> points,
        bool wait = true,
        WriteOrderingType? ordering = null,
        ShardKeySelector? shardKeySelector = null,
        CancellationToken cancellationToken = default
    );

    Task<IReadOnlyList<ScoredPoint>> SearchAsync(
        string collectionName,
        ReadOnlyMemory<float> vector,
        Filter? filter = null,
        SearchParams? searchParams = null,
        ulong limit = 10,
        ulong offset = 0,
        WithPayloadSelector? payloadSelector = null,
        WithVectorsSelector? vectorsSelector = null,
        float? scoreThreshold = null,
        string? vectorName = null,
        ReadConsistency? readConsistency = null,
        ShardKeySelector? shardKeySelector = null,
        ReadOnlyMemory<uint>? sparseIndices = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default
    );
}

[ExcludeFromCodeCoverage]
public class QdrantClientWrapper(
    QdrantClient client
) : IQdrantClient
{
    private readonly QdrantClient _client = client;

    public Task<UpdateResult> UpsertAsync(
        string collectionName,
        IReadOnlyList<PointStruct> points,
        bool wait = true,
        WriteOrderingType? ordering = null,
        ShardKeySelector? shardKeySelector = null,
        CancellationToken cancellationToken = default
    ) =>
        _client.UpsertAsync(
            collectionName,
            points,
            wait,
            ordering,
            shardKeySelector,
            cancellationToken
        );

    public Task<IReadOnlyList<ScoredPoint>> SearchAsync(
        string collectionName,
        ReadOnlyMemory<float> vector,
        Filter? filter = null,
        SearchParams? searchParams = null,
        ulong limit = 10,
        ulong offset = 0,
        WithPayloadSelector? payloadSelector = null,
        WithVectorsSelector? vectorsSelector = null,
        float? scoreThreshold = null,
        string? vectorName = null,
        ReadConsistency? readConsistency = null,
        ShardKeySelector? shardKeySelector = null,
        ReadOnlyMemory<uint>? sparseIndices = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default
    ) =>
        _client.SearchAsync(
            collectionName,
            vector,
            filter,
            searchParams,
            limit,
            offset,
            payloadSelector,
            vectorsSelector,
            scoreThreshold,
            vectorName,
            readConsistency,
            shardKeySelector,
            sparseIndices,
            timeout,
            cancellationToken
        );
}
