using Moq;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Qdrant.Client.Grpc;
using Application.Services;
using Persistence.Database;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using Domain.Persistence.Configuration;
using SmartExcelAnalyzer.Tests.TestUtilities;

namespace SmartExcelAnalyzer.Tests.Persistence.Database;

public class QdrantDatabaseWrapperTests
{
    private static readonly float[] item = [1.0f, 2.0f];
    private static readonly float[] itemArray = [3.0f, 4.0f];
    private readonly ExcelFileService ExcelReader = new();
    private readonly Mock<IQdrantClient> _mockClient = new();
    private readonly Mock<IOptions<DatabaseOptions>> _mockOptions = new();
    private readonly Mock<ILogger<QdrantDatabaseWrapper>> _mockLogger = new();
    private QdrantDatabaseWrapper Sut => new(
        _mockClient.Object,
        _mockOptions.Object,
        _mockLogger.Object);

    public QdrantDatabaseWrapperTests()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        _mockOptions
            .Setup(o => o.Value)
            .Returns(new DatabaseOptions
            {
                MAX_CONNECTION_COUNT = 5,
                SAVE_BATCH_SIZE = 100,
                CollectionName = "testCollection",
                CollectionNameTwo = "testSummaryCollection"
            });
    }

    [Fact]
    public async Task StoreVectorsAsync_ShouldCreateAndInsertRows()
    {
        var rows = new List<ConcurrentDictionary<string, object>>
        {
            new() { ["embedding"] = item, ["document_id"] = "testDocId" },
            new() { ["embedding"] = itemArray, ["document_id"] = "testDocId" }
        };
        _mockClient.Setup(c => c.UpsertAsync(
            It.IsAny<string>(),
            It.IsAny<IReadOnlyList<PointStruct>>(),
            It.IsAny<bool>(),
            It.IsAny<WriteOrderingType?>(),
            It.IsAny<ShardKeySelector?>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UpdateResult() { Status = UpdateStatus.Completed });

        var result = await Sut.StoreVectorsAsync(rows);

        result.Should().NotBeNull();
        result.Should().NotBeEmpty();
        _mockClient.Verify(c => c.UpsertAsync(
            It.IsAny<string>(),
            It.IsAny<IReadOnlyList<PointStruct>>(),
            It.IsAny<bool>(),
            It.IsAny<WriteOrderingType?>(),
            It.IsAny<ShardKeySelector?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task StoreSummaryAsync_ShouldStoreSummarySuccessfully()
    {
        var documentId = "testDocId";
        var summary = new ConcurrentDictionary<string, object> { ["key"] = "value" };

        _mockClient.Setup(c => c.UpsertAsync(
            It.IsAny<string>(),
            It.IsAny<IReadOnlyList<PointStruct>>(),
            It.IsAny<bool>(),
            It.IsAny<WriteOrderingType?>(),
            It.IsAny<ShardKeySelector?>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UpdateResult() { Status = UpdateStatus.Completed });

        var result = await Sut.StoreSummaryAsync(documentId, summary);

        result.Should().Be(1);
        _mockClient.Verify(c => c.UpsertAsync(
            It.IsAny<string>(),
            It.IsAny<IReadOnlyList<PointStruct>>(),
            It.IsAny<bool>(),
            It.IsAny<WriteOrderingType?>(),
            It.IsAny<ShardKeySelector?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task StoreSummaryAsync_ShouldReturnNullOnException()
    {
        var documentId = "testDocId";
        var summary = new ConcurrentDictionary<string, object> { ["key"] = "value" };

        _mockClient.Setup(c => c.UpsertAsync(
            It.IsAny<string>(),
            It.IsAny<IReadOnlyList<PointStruct>>(),
            It.IsAny<bool>(),
            It.IsAny<WriteOrderingType?>(),
            It.IsAny<ShardKeySelector?>(),
            It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Test exception"));

        var result = await Sut.StoreSummaryAsync(documentId, summary);

        result.Should().BeNull();
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => true),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()!),
            Times.Once);
    }

    [Fact]
    public async Task GetRelevantDocumentsAsync_ShouldReturnRelevantDocuments()
    {
        var documentId = "testDocId";
        var queryVector = new float[] { 1.0f, 2.0f };
        var topRelevantCount = 5;
        var excelData = TestDataGenerator.GenerateLargeDataSet(3, ["key"]).ToList();
        var mockSearchResult = excelData.Select(data => new ScoredPoint
        {
            Id = new PointId(),
            Vectors = queryVector,
            Payload = { ["content"] = new Value { StringValue = JsonSerializer.Serialize(data) } }
        }).ToList();

        _mockClient
            .Setup(c =>
                c.SearchAsync(
                    It.IsAny<string>(),
                    It.IsAny<ReadOnlyMemory<float>>(),
                    It.IsAny<Filter?>(),
                    It.IsAny<SearchParams?>(),
                    It.IsAny<ulong>(),
                    It.IsAny<ulong>(),
                    It.IsAny<WithPayloadSelector?>(),
                    It.IsAny<WithVectorsSelector?>(),
                    It.IsAny<float?>(),
                    It.IsAny<string?>(),
                    It.IsAny<ReadConsistency?>(),
                    It.IsAny<ShardKeySelector?>(),
                    It.IsAny<ReadOnlyMemory<uint>?>(),
                    It.IsAny<TimeSpan?>(),
                    It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockSearchResult);

        var result = await Sut.GetRelevantDocumentsAsync(documentId, queryVector, topRelevantCount);

        result.Should().NotBeEmpty();
        result.Count().Should().Be(3);
        result.Should().AllSatisfy(dict =>
        {
            dict.Should().ContainKey("key");
        });
    }

    [Fact]
    public async Task GetSummaryAsync_ShouldReturnSummary()
    {
        var documentId = "testDocId";
        var SummaryCollectionName = "testSummaryCollection";
        var summaryContent = new ConcurrentDictionary<string, object> { ["key"] = "\"value\"" };
        var point = new ScoredPoint
        {
            Id = new PointId(),
            Vectors = new float[1],
            Payload = { ["content"] = new Value { StringValue = JsonSerializer.Serialize(summaryContent) }, ["document_id"] = new Value { StringValue = documentId } }

        };
        var mockSearchResult = new List<ScoredPoint> { point };

        _mockOptions.Setup(o => o.Value).Returns(new DatabaseOptions
        {
            CollectionNameTwo = SummaryCollectionName
        });
        _mockClient
            .Setup(c =>
                c.SearchAsync(
                    It.Is<string>(s => s == SummaryCollectionName),
                    It.Is<ReadOnlyMemory<float>>(v => v.Length == 1),
                    It.Is<Filter?>(f => f != null && f.ToString().Contains(documentId)),
                    It.IsAny<SearchParams?>(),
                    It.IsAny<ulong>(),
                    It.IsAny<ulong>(),
                    It.IsAny<WithPayloadSelector?>(),
                    It.IsAny<WithVectorsSelector?>(),
                    It.IsAny<float?>(),
                    It.IsAny<string?>(),
                    It.IsAny<ReadConsistency?>(),
                    It.IsAny<ShardKeySelector?>(),
                    It.IsAny<ReadOnlyMemory<uint>?>(),
                    It.IsAny<TimeSpan?>(),
                    It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockSearchResult);

        var result = await Sut.GetSummaryAsync(documentId);

        result.Should().NotBeEmpty();
        result.Should().ContainKey("key");
    }

    [Fact]
    public async Task GetSummaryAsync_ShouldReturnEmptyDictionaryWhenNoSummaryFound()
    {
        var documentId = "testDocId";
        var mockSearchResult = new List<ScoredPoint>();

        _mockClient
            .Setup(c =>
                c.SearchAsync(
                    It.IsAny<string>(),
                    It.IsAny<ReadOnlyMemory<float>>(),
                    It.IsAny<Filter>(),
                    It.IsAny<SearchParams?>(),
                    It.IsAny<ulong>(),
                    It.IsAny<ulong>(),
                    It.IsAny<WithPayloadSelector>(),
                    It.IsAny<WithVectorsSelector?>(),
                    It.IsAny<float?>(),
                    It.IsAny<string?>(),
                    It.IsAny<ReadConsistency>(),
                    It.IsAny<ShardKeySelector>(),
                    It.IsAny<ReadOnlyMemory<uint>>(),
                    It.IsAny<TimeSpan?>(),
                    It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockSearchResult);

        var result = await Sut.GetSummaryAsync(documentId);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task StoreVectorsAsync_ShouldHandleExceptionDuringInsertion()
    {
        var rows = new List<ConcurrentDictionary<string, object>>
        {
            new() { ["embedding"] = item }
        };
        _mockClient
            .Setup(c => c.UpsertAsync(
                It.IsAny<string>(),
                It.IsAny<IReadOnlyList<PointStruct>>(),
                It.IsAny<bool>(),
                It.IsAny<WriteOrderingType?>(),
                It.IsAny<ShardKeySelector?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Test exception"));

        var result = await Sut.StoreVectorsAsync(rows);
        result.Should().BeNull();
    }

    [Fact]
    public async Task StoreVectorsAsync_ShouldHandleEmptyInputCollection()
    {
        var emptyRows = new List<ConcurrentDictionary<string, object>>();

        var result = await Sut.StoreVectorsAsync(emptyRows);

        _mockClient.Verify(c => c.UpsertAsync(
            It.IsAny<string>(),
            It.IsAny<IReadOnlyList<PointStruct>>(),
            It.IsAny<bool>(),
            It.IsAny<WriteOrderingType?>(),
            It.IsAny<ShardKeySelector?>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task StoreVectorsAsync_ShouldHandleLargeInputCollection()
    {
        var largeDataSet = TestDataGenerator.GenerateLargeDataSet(10000).ToList();
        _mockOptions.Setup(o => o.Value).Returns(new DatabaseOptions
        {
            SAVE_BATCH_SIZE = 1000,
            CollectionName = "testCollection",
            MAX_CONNECTION_COUNT = 5
        });

        _mockClient.Setup(c => c.UpsertAsync(
            It.IsAny<string>(),
            It.IsAny<IReadOnlyList<PointStruct>>(),
            It.IsAny<bool>(),
            It.IsAny<WriteOrderingType?>(),
            It.IsAny<ShardKeySelector?>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UpdateResult() { Status = UpdateStatus.Completed });

        var result = await Sut.StoreVectorsAsync(largeDataSet);

        _mockClient.Verify(c => c.UpsertAsync(
            It.IsAny<string>(),
            It.IsAny<IReadOnlyList<PointStruct>>(),
            It.IsAny<bool>(),
            It.IsAny<WriteOrderingType?>(),
            It.IsAny<ShardKeySelector?>(),
            It.IsAny<CancellationToken>()), Times.Exactly(10));
    }

    [Fact]
    public async Task StoreVectorsAsync_ShouldHandleDifferentBatchSizes()
    {
        var file = TestDataGenerator.GenerateExcelFile(3);
        var dataSet = await ExcelReader.PrepareExcelFileForLLMAsync(file);
        _mockOptions.Setup(o => o.Value).Returns(new DatabaseOptions
        {
            SAVE_BATCH_SIZE = 200,
            CollectionName = "testCollection",
            MAX_CONNECTION_COUNT = 5
        });

        _mockClient.Setup(c => c.UpsertAsync(
            It.IsAny<string>(),
            It.IsAny<IReadOnlyList<PointStruct>>(),
            It.IsAny<bool>(),
            It.IsAny<WriteOrderingType?>(),
            It.IsAny<ShardKeySelector?>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new UpdateResult() { Status = UpdateStatus.Completed });

        var result = await Sut.StoreVectorsAsync(dataSet.Rows!);

        result.Should().NotBeNull();
        _mockClient.Verify(c => c.UpsertAsync(
            It.IsAny<string>(),
            It.Is<IReadOnlyList<PointStruct>>(points => points.Count == 3),
            It.IsAny<bool>(),
            It.IsAny<WriteOrderingType?>(),
            It.IsAny<ShardKeySelector?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetRelevantDocumentsAsync_ShouldHandleEmptyResult()
    {
        var documentId = "testDocId";
        var queryVector = new float[] { 1.0f, 2.0f };
        var topRelevantCount = 5;

        _mockClient
            .Setup(c =>
                c.SearchAsync(
                    It.IsAny<string>(),
                    It.IsAny<ReadOnlyMemory<float>>(),
                    It.IsAny<Filter>(),
                    It.IsAny<SearchParams?>(),
                    It.IsAny<ulong>(),
                    It.IsAny<ulong>(),
                    It.IsAny<WithPayloadSelector>(),
                    It.IsAny<WithVectorsSelector?>(),
                    It.IsAny<float?>(),
                    It.IsAny<string?>(),
                    It.IsAny<ReadConsistency>(),
                    It.IsAny<ShardKeySelector>(),
                    It.IsAny<ReadOnlyMemory<uint>>(),
                    It.IsAny<TimeSpan?>(),
                    It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var result = await Sut.GetRelevantDocumentsAsync(documentId, queryVector, topRelevantCount);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetRelevantDocumentsAsync_ShouldHandleExceptionDuringSearch()
    {
        var documentId = "testDocId";
        var queryVector = new float[] { 1.0f, 2.0f };
        var topRelevantCount = 5;
        var collectionName = "testCollection";
        _mockOptions.Setup(o => o.Value).Returns(new DatabaseOptions
        {
            CollectionName = collectionName
        });
        _mockClient
            .Setup(c =>
                c.SearchAsync(
                    It.Is<string>(s => s == collectionName),
                    It.Is<ReadOnlyMemory<float>>(v => v.ToArray().SequenceEqual(queryVector)),
                    It.Is<Filter?>(f => f != null && f.ToString().Contains(documentId)),
                    It.IsAny<SearchParams?>(),
                    It.Is<ulong>(l => l == (ulong)topRelevantCount),
                    It.IsAny<ulong>(),
                    It.IsAny<WithPayloadSelector?>(),
                    It.IsAny<WithVectorsSelector?>(),
                    It.IsAny<float?>(),
                    It.IsAny<string?>(),
                    It.IsAny<ReadConsistency?>(),
                    It.IsAny<ShardKeySelector?>(),
                    It.IsAny<ReadOnlyMemory<uint>?>(),
                    It.IsAny<TimeSpan?>(),
                    It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Test exception"));

        await Assert.ThrowsAsync<Exception>(() =>
            Sut.GetRelevantDocumentsAsync(documentId, queryVector, topRelevantCount));
    }

    [Fact]
    public async Task GetSummaryAsync_ShouldHandleInvalidJsonContent()
    {
        var documentId = "testDocId";
        var point = new ScoredPoint { Id = new PointId(), Vectors = new float[1] };
        point.Payload.Add("content", new Value { StringValue = "Invalid JSON" });
        var mockSearchResult = new List<ScoredPoint> { point };

        _mockClient
            .Setup(c =>
                c.SearchAsync(
                    It.IsAny<string>(),
                    It.IsAny<ReadOnlyMemory<float>>(),
                    It.IsAny<Filter>(),
                    It.IsAny<SearchParams?>(),
                    It.IsAny<ulong>(),
                    It.IsAny<ulong>(),
                    It.IsAny<WithPayloadSelector>(),
                    It.IsAny<WithVectorsSelector?>(),
                    It.IsAny<float?>(),
                    It.IsAny<string?>(),
                    It.IsAny<ReadConsistency>(),
                    It.IsAny<ShardKeySelector>(),
                    It.IsAny<ReadOnlyMemory<uint>>(),
                    It.IsAny<TimeSpan?>(),
                    It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockSearchResult);

        var result = await Sut.GetSummaryAsync(documentId);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetSummaryAsync_ShouldHandleMissingContentKey()
    {
        var documentId = "testDocId";
        var point = new ScoredPoint { Id = new PointId(), Vectors = new float[1] };
        point.Payload.Add("other_key", new Value { StringValue = "Some value" });
        var mockSearchResult = new List<ScoredPoint> { point };

        _mockClient
            .Setup(c =>
                c.SearchAsync(
                    It.IsAny<string>(),
                    It.IsAny<ReadOnlyMemory<float>>(),
                    It.IsAny<Filter>(),
                    It.IsAny<SearchParams?>(),
                    It.IsAny<ulong>(),
                    It.IsAny<ulong>(),
                    It.IsAny<WithPayloadSelector>(),
                    It.IsAny<WithVectorsSelector?>(),
                    It.IsAny<float?>(),
                    It.IsAny<string?>(),
                    It.IsAny<ReadConsistency>(),
                    It.IsAny<ShardKeySelector>(),
                    It.IsAny<ReadOnlyMemory<uint>>(),
                    It.IsAny<TimeSpan?>(),
                    It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockSearchResult);

        var result = await Sut.GetSummaryAsync(documentId);

        result.Should().BeEmpty();
    }
}