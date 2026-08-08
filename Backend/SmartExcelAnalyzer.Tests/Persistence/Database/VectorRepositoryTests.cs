using Moq;
using System.Text.Json;
using FluentAssertions;
using Domain.Persistence;
using Persistence.Database;
using Domain.Persistence.DTOs;
using Persistence.Repositories;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using Domain.Persistence.Configuration;
using SmartExcelAnalyzer.Tests.TestUtilities;
using Microsoft.Extensions.Caching.Memory;

namespace SmartExcelAnalyzer.Tests.Persistence.Database;

public class VectorRepoAddTests
{
    private const int SAVE_BATCH_SIZE = 10;
    private const int COMPUTE_BATCH_SIZE = 10;
    private readonly Mock<IMemoryCache> _cacheMock = new();
    private readonly Mock<IDatabaseWrapper> _databaseMock = new();
    private readonly Mock<ILLMRepository> _llmRepositoryMock = new();
    private readonly Mock<ILogger<VectorRepository>> _loggerMock = new();
    private readonly Mock<IOptions<LLMServiceOptions>> _llmOptionsMock = new();
    private readonly Mock<IOptions<DatabaseOptions>> _databaseOptionsMock = new();
    private VectorRepository Sut => new(_databaseMock.Object, _loggerMock.Object, _llmRepositoryMock.Object, _llmOptionsMock.Object, _databaseOptionsMock.Object, _cacheMock.Object);

    private static readonly float[] singleArray = [1.0f];

    public VectorRepoAddTests()
    {
        _llmOptionsMock.Setup(o => o.Value).Returns(new LLMServiceOptions { LLM_SERVICE_URL = "http://test.com", COMPUTE_BATCH_SIZE = COMPUTE_BATCH_SIZE, LLM_SERVICE_URLS = ["http://test.com", "http://test.com:1"] });
        _databaseOptionsMock.Setup(o => o.Value).Returns(new DatabaseOptions { SAVE_BATCH_SIZE = SAVE_BATCH_SIZE });
    }

    [Fact]
    public async Task SaveDocumentAsync_ShouldSucceedWhenSavingRowsSucceeds_ButSavingSummaryFails()
    {
        const string documentId = "1";
        var data = new SummarizedExcelData
        {
            Rows =
            [
                new ConcurrentDictionary<string, object> { ["col1"] = "val1" }
            ],
            Summary = new ConcurrentDictionary<string, object> { ["sum"] = 10 }
        };
        _llmRepositoryMock.Setup(l => l.ComputeBatchEmbeddings(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([[1.0f]]);
        _databaseMock.SetupSequence(c => c.StoreVectorsAsync(It.IsAny<ConcurrentBag<ConcurrentDictionary<string, object>>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(documentId);

        _databaseMock.Setup(c => c.StoreSummaryAsync(It.Is<string>(y => y == documentId), It.IsAny<ConcurrentDictionary<string, object>>(), It.IsAny<CancellationToken>())).ReturnsAsync(-1);

        var result = await Sut.SaveDocumentAsync(data);

        result.Should().NotBeNull();
    }

    [Fact]
    public async Task SaveDocumentAsync_ShouldReturnNullWhenInputDataIsNull()
    {
        var result = await Sut.SaveDocumentAsync(null!);

        result.Should().BeNull();
    }

    [Fact]
    public async Task SaveDocumentAsync_ShouldReturnNullWhenInputDataIsEmpty()
    {
        (await Sut.SaveDocumentAsync(null!)).Should().BeNull();
    }

    [Fact]
    public async Task QueryVectorData_ShouldReturnRelevantRowsAndSummary()
    {
        var documentId = "testDoc";
        var queryVector = new float[] { 1.0f, 2.0f, 3.0f };
        var documents = new List<Document>
        {
            new() { Id = documentId, Content = "{\"col1\":\"val1\"}", Embedding = [1.0f, 2.0f, 3.0f] }
        };
        var summaries = new List<Summary>
        {
            new() { Id = documentId, Content = "{\"sum\":10}" }
        };
        _databaseMock.Setup(c => c.GetRelevantDocumentsAsync(It.IsAny<string>(), It.IsAny<float[]>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(documents.Select(d => new ConcurrentDictionary<string, object>
            {
                ["content"] = JsonSerializer.Deserialize<ConcurrentDictionary<string, object>>(d.Content)!
            }));
        var contentDict = JsonSerializer.Deserialize<ConcurrentDictionary<string, object>>(summaries.First().Content)!;
        _databaseMock.Setup(c => c.GetSummaryAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConcurrentDictionary<string, object>
            {
                ["content"] = contentDict
            });
        var result = await Sut.QueryVectorDataAsync(documentId, queryVector);

        result.Should().NotBeNull();
        result.Rows.Should().HaveCount(1);
        result.Rows!.First().Should().ContainKey("content");
        result.Summary!["content"].ToString().Should().BeEquivalentTo(contentDict.ToString());
    }

    [Fact]
    public async Task QueryVectorData_ShouldReturnNullWhenNoRelevantDocumentsFound()
    {
        var documentId = "testDoc";
        var queryVector = new float[] { 1.0f, 2.0f, 3.0f };
        _databaseMock.Setup(c => c.GetRelevantDocumentsAsync(It.IsAny<string>(), It.IsAny<float[]>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        var result = await Sut.QueryVectorDataAsync(documentId, queryVector);

        result.Should().BeNull();
    }

    [Fact]
    public async Task QueryVectorData_ShouldReturnRowsWithoutSummaryWhenSummaryDoesNotExist()
    {
        var documentId = "testDoc";
        var queryVector = new float[] { 1.0f, 2.0f, 3.0f };
        var documents = new List<Document>
        {
            new() { Id = documentId, Content = "{\"col1\":\"val1\"}", Embedding = [1.0f, 2.0f, 3.0f] }
        };
        _databaseMock.Setup(c => c.GetRelevantDocumentsAsync(It.IsAny<string>(), It.IsAny<float[]>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(documents.Select(d => new ConcurrentDictionary<string, object>
            {
                ["content"] = JsonSerializer.Deserialize<ConcurrentDictionary<string, object>>(d.Content)!
            }));
        _databaseMock.Setup(c => c.GetSummaryAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ConcurrentDictionary<string, object>)null!);

        var result = await Sut.QueryVectorDataAsync(documentId, queryVector);

        result.Should().NotBeNull();
        result.Rows.Should().HaveCount(1);
        result.Rows!.First().Should().ContainKey("content");
        result.Summary.Should().BeNull();
    }

    [Fact]
    public async Task QueryVectorData_ShouldReturnNullWhenDocumentIdIsNullOrEmpty()
    {
        var queryVector = new float[] { 1.0f, 2.0f, 3.0f };

        var result = await Sut.QueryVectorDataAsync(string.Empty, queryVector);

        result.Should().BeNull();
    }

    [Fact]
    public async Task QueryVectorData_ShouldReturnNullWhenQueryVectorIsNull()
    {
        var documentId = "testDoc";

        var result = await Sut.QueryVectorDataAsync(documentId, null!);

        result.Should().BeNull();
    }

    [Fact]
    public async Task QueryVectorData_ShouldReturnNullWhenQueryVectorIsEmpty()
    {
        var documentId = "testDoc";
        var emptyQueryVector = Array.Empty<float>();

        var result = await Sut.QueryVectorDataAsync(documentId, emptyQueryVector);

        result.Should().BeNull();
    }

    [Fact]
    public async Task SaveDocumentAsync_ShouldReturnNullWhenRowsAreEmpty()
    {
        var data = new SummarizedExcelData
        {
            Rows = [],
            Summary = new ConcurrentDictionary<string, object> { ["sum"] = 10 }
        };

        var result = await Sut.SaveDocumentAsync(data);

        result.Should().BeNullOrEmpty();
    }

    [Fact]
    public async Task SaveDocumentAsync_ShouldSucceedWhenSummaryIsNull()
    {
        const string documentId = "1";
        var data = new SummarizedExcelData
        {
            Rows =
            [
                new ConcurrentDictionary<string, object> { ["col1"] = "val1" }
            ],
            Summary = null
        };
        _llmRepositoryMock.Setup(l => l.ComputeBatchEmbeddings(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([[1.0f]]);
        _databaseMock.Setup(c => c.StoreVectorsAsync(It.IsAny<ConcurrentBag<ConcurrentDictionary<string, object>>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(documentId);

        var result = await Sut.SaveDocumentAsync(data);

        result.Should().Be(documentId);
    }

    [Fact]
    public async Task SaveDocumentAsync_ShouldHandle_GeneralException()
    {
        var data = new SummarizedExcelData
        {
            Rows =
            [
                new ConcurrentDictionary<string, object> { ["col1"] = "val1" }
            ],
            Summary = new ConcurrentDictionary<string, object> { ["sum"] = 10 }
        };
        var cts = new CancellationTokenSource();
        _databaseMock
            .Setup(c => c.StoreVectorsAsync(It.IsAny<ConcurrentBag<ConcurrentDictionary<string, object>>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Test exception"));

        var result = await Sut.SaveDocumentAsync(data, cancellationToken: cts.Token);

        result.Should().BeNullOrEmpty();
        _loggerMock.VerifyLog(LogLevel.Warning, "Failed to save vectors to the database", Times.AtLeastOnce());
    }

    [Fact]
    public async Task SaveDocumentAsync_ShouldReportProgressCorrectly()
    {
        const string documentId = "1";
        var data = new SummarizedExcelData
        {
            Rows = new ConcurrentBag<ConcurrentDictionary<string, object>>(Enumerable.Range(0, 100).Select(i => new ConcurrentDictionary<string, object> { ["col1"] = $"val{i}" })),
            Summary = new ConcurrentDictionary<string, object> { ["sum"] = 10 }
        };
        _llmRepositoryMock.Setup(l => l.ComputeBatchEmbeddings(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Enumerable.Range(0, 10).Select(_ => singleArray).ToArray());
        _databaseMock.Setup(c => c.StoreVectorsAsync(It.IsAny<ConcurrentBag<ConcurrentDictionary<string, object>>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(documentId);

        var progressReports = new List<(double, double)>();
        var progress = new Progress<(double, double)>(progressReports.Add);

        await Sut.SaveDocumentAsync(data, progress);

        progressReports.Should().NotBeEmpty();
        progressReports.Should().Contain((1, 1));
        _loggerMock.VerifyLog(LogLevel.Information, "Starting to save document");
        _loggerMock.VerifyLog(LogLevel.Information, "Computing embeddings for document with", Times.AtLeastOnce());
        _loggerMock.VerifyLog(LogLevel.Information, "Saved document with id 1 to the database");
    }

    [Fact]
    public async Task QueryVectorData_ShouldHandleExceptionInGetSummaryAsync()
    {
        var documentId = "testDoc";
        var queryVector = new float[] { 1.0f, 2.0f, 3.0f };
        var documents = new List<Document>
        {
            new() { Id = documentId, Content = "{\"col1\":\"val1\"}", Embedding = [1.0f, 2.0f, 3.0f] }
        };
        _databaseMock.Setup(c => c.GetRelevantDocumentsAsync(It.IsAny<string>(), It.IsAny<float[]>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(documents.Select(d => new ConcurrentDictionary<string, object>
            {
                ["content"] = JsonSerializer.Deserialize<ConcurrentDictionary<string, object>>(d.Content)!
            }));
        _databaseMock.Setup(c => c.GetSummaryAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ConcurrentDictionary<string, object>)null!);

        var result = await Sut.QueryVectorDataAsync(documentId, queryVector);

        result.Should().NotBeNull();
        result.Rows.Should().HaveCount(1);
        result.Summary.Should().BeNull();
        _loggerMock.VerifyLog(LogLevel.Warning, "Failed to query the summary of the document");
    }

    [Fact]
    public async Task SaveDocumentAsync_ShouldHandleInconsistentDocumentIds()
    {
        var data = new SummarizedExcelData
        {
            Rows = new ConcurrentBag<ConcurrentDictionary<string, object>>(Enumerable.Range(0, 20).Select(i => new ConcurrentDictionary<string, object> { ["col1"] = $"val{i}" })),
            Summary = new ConcurrentDictionary<string, object> { ["sum"] = 10 }
        };
        _llmRepositoryMock.Setup(l => l.ComputeBatchEmbeddings(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Enumerable.Range(0, 10).Select(_ => new float[] { 1.0f }).ToArray());
        _databaseMock.SetupSequence(c => c.StoreVectorsAsync(It.IsAny<ConcurrentBag<ConcurrentDictionary<string, object>>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("1")
            .ReturnsAsync("2");
        _databaseMock.Setup(c => c.StoreSummaryAsync(It.IsAny<string>(), It.IsAny<ConcurrentDictionary<string, object>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var result = await Sut.SaveDocumentAsync(data);

        result.Should().Be("1");
        _loggerMock.VerifyLog(LogLevel.Warning, "Inconsistent document IDs across batches");
    }

    [Fact]
    public async Task SaveDocumentAsync_ShouldHandleNullEmbeddings_Empty()
    {
        const string documentId = "1";
        var data = new SummarizedExcelData
        {
            Rows =
            [
                new ConcurrentDictionary<string, object> { ["col1"] = "val1" }
            ],
            Summary = new ConcurrentDictionary<string, object> { ["sum"] = 10 }
        };
        _llmRepositoryMock.Setup(l => l.ComputeBatchEmbeddings(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([null]);
        _databaseMock.Setup(c => c.StoreVectorsAsync(It.IsAny<ConcurrentBag<ConcurrentDictionary<string, object>>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(documentId);

        var result = await Sut.SaveDocumentAsync(data);

        result.Should().Be(documentId);
        _loggerMock.VerifyLog(LogLevel.Warning, "Embedding at index");
    }

    [Fact]
    public async Task SaveDocumentAsync_ShouldHandleNullEmbeddings_AllNull()
    {
        const string documentId = "1";
        var data = new SummarizedExcelData
        {
            Rows =
            [
                new ConcurrentDictionary<string, object> { ["col1"] = "val1" }
            ],
            Summary = new ConcurrentDictionary<string, object> { ["sum"] = 10 }
        };
        _llmRepositoryMock.Setup(l => l.ComputeBatchEmbeddings(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((float[][])null!);
        _databaseMock.Setup(c => c.StoreVectorsAsync(It.IsAny<ConcurrentBag<ConcurrentDictionary<string, object>>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(documentId);

        var result = await Sut.SaveDocumentAsync(data);

        result.Should().BeNullOrEmpty();
        _loggerMock.VerifyLog(LogLevel.Warning, "Failed to save vectors to the database", Times.AtLeastOnce());
    }

    [Fact]
    public async Task QueryVectorData_ShouldReturnMaximumNumberOfRelevantDocuments()
    {
        var documentId = "testDoc";
        var queryVector = new float[] { 1.0f, 2.0f, 3.0f };
        var maxRelevantCount = 20;
        var documents = Enumerable.Range(0, maxRelevantCount).Select(i => new Document
        {
            Id = documentId,
            Content = $"{{\"col1\":\"val{i}\"}}",
            Embedding = [1.0f, 2.0f, 3.0f]
        }).ToList();

        _databaseMock
            .Setup(c => c.GetRelevantDocumentsAsync(It.IsAny<string>(), It.IsAny<float[]>(), It.Is<int>(x => x == maxRelevantCount), It.IsAny<CancellationToken>()))
            .ReturnsAsync(documents.Select(d => new ConcurrentDictionary<string, object>
            {
                ["content"] = JsonSerializer.Deserialize<ConcurrentDictionary<string, object>>(d.Content)!
            }));
        _databaseMock.Setup(c => c.GetSummaryAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ConcurrentDictionary<string, object> { ["sum"] = 10 });

        var result = await Sut.QueryVectorDataAsync(documentId, queryVector, maxRelevantCount);

        result.Should().NotBeNull();
        result.Rows.Should().HaveCount(maxRelevantCount);
    }
}
