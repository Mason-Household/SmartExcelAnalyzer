using FluentAssertions;
using Application.Services;
using Domain.Persistence.DTOs;
using Persistence.Repositories;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Builder;
using Domain.Persistence.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ProgramExtensions = API.Extensions.ProgramExtensions;

namespace SmartExcelAnalyzer.Tests.API.Extensions;

public class ProgramExtensionsTests
{
    [Fact]
    public void ConfigureEnvironmentVariables_ShouldAddEnvironmentVariables()
    {
        var builder = WebApplication.CreateBuilder();
        builder = ProgramExtensions.AddSmartExcelFileAnalyzerVariables(builder);

        builder.Configuration["LLMServiceOptions:LLM_SERVICE_URL"].Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void ConfigureLogging_ShouldConfigureLogging()
    {
        var builder = WebApplication.CreateBuilder();
        builder = ProgramExtensions.ConfigureLogging(builder);

        var serviceProvider = builder.Services.BuildServiceProvider();
        var loggerFactory = serviceProvider.GetService<ILoggerFactory>();

        loggerFactory.Should().NotBeNull();
    }

    [Fact]
    public void ConfigureHttpClient_ShouldAddHttpClient()
    {
        var builder = WebApplication.CreateBuilder();
        builder = ProgramExtensions.ConfigureHttpClient(builder);

        var serviceProvider = builder.Services.BuildServiceProvider();
        var httpClientFactory = serviceProvider.GetService<IHttpClientFactory>();

        httpClientFactory.Should().NotBeNull();
    }

    [Fact]
    public void ConfigureDatabase_ShouldConfigureDatabaseOptions()
    {
        var builder = WebApplication.CreateBuilder();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                {"DatabaseOptions:PORT", "6333"},
                {"DatabaseOptions:SAVE_BATCH_SIZE", "100"},
                {"DatabaseOptions:QDRANT_API_KEY", "test-key"},
                {"DatabaseOptions:HOST", "localhost"},
                {"DatabaseOptions:MAX_CONNECTION_COUNT", "10"},
                {"DatabaseOptions:DatabaseName", "testdb"},
                {"DatabaseOptions:CollectionName", "testcollection"},
                {"DatabaseOptions:CollectionNameTwo", "testcollection2"}
            })
            .Build();

        builder.Configuration.AddConfiguration(configuration);
        builder = ProgramExtensions.ConfigureDatabase(builder);

        var serviceProvider = builder.Services.BuildServiceProvider();
        var options = serviceProvider.GetService<IOptions<DatabaseOptions>>();

        options.Should().NotBeNull();
        options!.Value.PORT.Should().Be(6333);
        options!.Value.SAVE_BATCH_SIZE.Should().Be(100);
        options!.Value.QDRANT_API_KEY.Should().Be("test-key");
        options!.Value.HOST.Should().Be("localhost");
        options!.Value.MAX_CONNECTION_COUNT.Should().Be(10);
        options!.Value.DatabaseName.Should().Be("testdb");
        options!.Value.CollectionName.Should().Be("testcollection");
        options!.Value.CollectionNameTwo.Should().Be("testcollection2");
    }

    [Fact]
    public void ConfigureLLMService_ShouldConfigureLLMOptions()
    {
        var builder = WebApplication.CreateBuilder();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                {"LLMServiceOptions:LLM_SERVICE_URL", "http://localhost:5000"},
                {"LLMServiceOptions:LLM_SERVICE_URLS:0", "http://localhost:5000"},
                {"LLMServiceOptions:LLM_SERVICE_URLS:1", "http://localhost:5002"}
            })
            .Build();

        builder.Configuration.AddConfiguration(configuration);
        builder = ProgramExtensions.ConfigureLLMService(builder);

        var serviceProvider = builder.Services.BuildServiceProvider();
        var options = serviceProvider.GetService<IOptions<LLMServiceOptions>>();

        options.Should().NotBeNull();
        options!.Value.LLM_SERVICE_URLS.Should().NotBeEmpty();
        options!.Value.LLM_SERVICE_URLS.Should().HaveCount(3);
        options!.Value.LLM_SERVICE_URLS.Should().Contain("http://localhost:5000");
        options!.Value.LLM_SERVICE_URLS.Should().Contain("http://localhost:5002");
    }

    [Fact]
    public void ConfigureMediatR_ShouldConfigureMediatR()
    {
        var builder = WebApplication.CreateBuilder();
        builder = ProgramExtensions.ConfigureMediatR(builder);

        var serviceProvider = builder.Services.BuildServiceProvider();
        var mediator = serviceProvider.GetService<MediatR.IMediator>();

        mediator.Should().NotBeNull();
    }

    [Fact]
    public void ConfigureServices_ShouldAddRequiredServices()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddHttpClient();
        builder = ProgramExtensions.ConfigureServices(builder);

        var serviceProvider = builder.Services.BuildServiceProvider();
        var excelFileService = serviceProvider.GetService<IExcelFileService>();
        var webRepository = serviceProvider.GetService<IWebRepository<QueryAnswer>>();

        excelFileService.Should().NotBeNull();
        webRepository.Should().NotBeNull();
    }
}