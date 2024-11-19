using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Cors.Infrastructure;
using API.Extensions;

namespace SmartExcelAnalyzer.Tests.API.Extensions;

public class ConfigurationExtensionsTests
{
    [Fact]
    public void ConfigureEnvironmentVariables_ShouldNotThrowException()
    {
        var builder = WebApplication.CreateBuilder();

        var exception = Record.Exception(() => builder.AddSmartExcelFileAnalyzerVariables());
        exception.Should().BeNull();
    }

    [Fact]
    public void ConfigureLogging_ShouldAddLoggingServices()
    {
        var builder = WebApplication.CreateBuilder();

        builder.ConfigureLogging();

        var serviceProvider = builder.Services.BuildServiceProvider();
        var loggerFactory = serviceProvider.GetService<ILoggerFactory>();
        loggerFactory.Should().NotBeNull();
    }

    [Fact]
    public void ConfigureHttpClient_ShouldAddHttpClient()
    {
        var builder = WebApplication.CreateBuilder();

        builder.ConfigureHttpClient();

        var serviceProvider = builder.Services.BuildServiceProvider();
        var httpClientFactory = serviceProvider.GetService<IHttpClientFactory>();
        httpClientFactory.Should().NotBeNull();
    }

    [Fact]
    public void ConfigureApiAccess_ShouldConfigureControllers()
    {
        var builder = WebApplication.CreateBuilder();

        builder.ConfigureApiAccess();

        var serviceProvider = builder.Services.BuildServiceProvider();
        var corsService = serviceProvider.GetService<ICorsService>();
        corsService.Should().NotBeNull();
    }
}