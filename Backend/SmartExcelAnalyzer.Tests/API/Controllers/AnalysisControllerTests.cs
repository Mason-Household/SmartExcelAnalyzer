using Moq;
using MediatR;
using System.Text;
using API.Controllers;
using FluentAssertions;
using Persistence.Hubs;
using Application.Queries;
using Application.Commands;
using Domain.Persistence.DTOs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;

namespace SmartExcelAnalyzer.Tests.API.Controllers;

public class AnalysisControllerTests
{
    private readonly Mock<IMediator> _mediatorMock = new();
    private readonly Mock<IProgressHubWrapper> _hubWrapperMock = new();
    private readonly Mock<IMemoryCache> _cacheMock = new();
    private AnalysisController Sut => new(_mediatorMock.Object, _hubWrapperMock.Object, _cacheMock.Object);

    public AnalysisControllerTests()
    {
        // IMemoryCache.Set is an extension method over CreateEntry, which returns
        // null on a bare mock and makes the controller throw.
        _cacheMock
            .Setup(c => c.CreateEntry(It.IsAny<object>()))
            .Returns(Mock.Of<ICacheEntry>());
    }

    [Fact]
    public async Task SubmitQuery_ReturnsOkResult_WhenQueryIsValid()
    {
        var query = new SubmitQuery { Query = "test query", DocumentId = "doc1" };
        var expectedResult = new QueryAnswer { Answer = "test answer" };
        _mediatorMock
            .Setup(m => m.Send(It.IsAny<SubmitQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResult);

        var result = await Sut.SubmitQuery(query);

        var okResult = Assert.IsType<OkObjectResult>(result);
        okResult.Value.Should().BeEquivalentTo(expectedResult);
    }

    [Fact]
    public async Task UploadFile_ReturnsOkResult_WhenFileIsValid()
    {
        var file = new FormFile(new MemoryStream(Encoding.UTF8.GetBytes("test file")), 0, 0, "file", "test.txt");
        var expectedResult = "doc1";
        _mediatorMock
            .Setup(m => m.Send(It.IsAny<UploadFileCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResult);

        var result = await Sut.UploadFile(file);

        var okResult = Assert.IsType<OkObjectResult>(result);
        var expectedObject = new { DocumentId = expectedResult };
        okResult.Value.Should().BeEquivalentTo(expectedObject);
    }
}
