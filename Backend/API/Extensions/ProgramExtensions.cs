using System.Text;
using Qdrant.Client;
using API.Properties;
using API.Middleware;
using API.Attributes;
using Persistence.Hubs;
using Application.Queries;
using Application.Services;
using Persistence.Database;
using Persistence.Repositories;
using Microsoft.OpenApi.Models;
using FluentValidation.AspNetCore;
using System.Diagnostics.CodeAnalysis;
using Domain.Persistence.Configuration;

namespace API.Extensions;

public static class ProgramExtensions
{
    [ExcludeFromCodeCoverage]
    public static WebApplication ConfigureSmartExcelAnalyzerProgram(string[] args) => WebApplication.CreateBuilder(args)
        .AddSmartExcelFileAnalyzerVariables()
        .ConfigureLogging()
        .ConfigureMediatR()
        .ConfigureSwagger()
        .ConfigureDatabase()
        .ConfigureServices()
        .ConfigureApiAccess()
        .ConfigureHttpClient()
        .ConfigureLLMService()
        .Build()
        .ConfigureCors()
        .ConfigureMiddleware() 
        .ConfigureProgressHub();
    
    public static WebApplicationBuilder AddSmartExcelFileAnalyzerVariables(this WebApplicationBuilder? builder)
    {
        builder ??= WebApplication.CreateBuilder();
        builder!.Configuration.AddJsonFile(ConfigurationConstants.AppSettingsJson, optional: true, reloadOnChange: true);
        builder.Configuration.AddJsonFile(
            string.Format(ConfigurationConstants.AppSettingsEnvironmentJson, builder.Environment.EnvironmentName), 
            optional: true, 
            reloadOnChange: true
        );
        builder.Configuration.AddEnvironmentVariables();
        return builder;
    }

    public static WebApplicationBuilder ConfigureLogging(this WebApplicationBuilder builder)
    {
        builder.Logging.AddConsole();
        builder.Services.AddLogging();
        builder.Services.AddApplicationInsightsTelemetry();
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        builder.Logging.AddConfiguration(builder.Configuration.GetSection(ConfigurationConstants.LoggingSection));
        return builder;
    }

    public static WebApplicationBuilder ConfigureHttpClient(this WebApplicationBuilder builder)
    {
        builder.Services.AddHttpClient(ConfigurationConstants.DefaultClientName, 
            client =>
            {
                client.Timeout = TimeSpan.FromMinutes(30);
            }
        );
        return builder;
    }

    public static WebApplicationBuilder ConfigureApiAccess(this WebApplicationBuilder builder)
    {
        builder.Services.AddSignalR();
        builder.Services.AddHealthChecks();
        builder.Services.AddControllers(options => options.AddCommonResponseTypes());
        builder.Services.AddCors(options =>
        {
            options.AddPolicy(ConfigurationConstants.AppCorsPolicy, builder =>
            {
                builder.AllowAnyOrigin()
                        .AllowAnyHeader()
                        .AllowAnyMethod();
            });
        });

        return builder;
    }

    public static WebApplicationBuilder ConfigureDatabase(this WebApplicationBuilder builder)
    {
        var databaseOptions = builder.Configuration.GetSection(ConfigurationConstants.DatabaseOptionsSection);
        builder.Services.Configure<DatabaseOptions>(databaseOptions);
        builder.Services
            .AddOptions<DatabaseOptions>()
            .Validate(options => options.PORT > 0, ConfigurationConstants.ValidationMessages.QdrantPortValidation)
            .Validate(options => options.SAVE_BATCH_SIZE > 0, ConfigurationConstants.ValidationMessages.QdrantBatchSizeValidation)
            .Validate(options => !string.IsNullOrEmpty(options.HOST), ConfigurationConstants.ValidationMessages.QdrantHostValidation)
            .Validate(options => options.MAX_CONNECTION_COUNT > 0, ConfigurationConstants.ValidationMessages.QdrantMaxConnectionValidation)
            .Validate(options => !string.IsNullOrEmpty(options.QDRANT_API_KEY), ConfigurationConstants.ValidationMessages.QdrantApiKeyValidation)
            .Validate(options => !string.IsNullOrEmpty(options.DatabaseName), ConfigurationConstants.ValidationMessages.QdrantDatabaseNameValidation)
            .Validate(options => !string.IsNullOrEmpty(options.CollectionName), ConfigurationConstants.ValidationMessages.QdrantCollectionNameValidation)
            .Validate(options => !string.IsNullOrEmpty(options.CollectionNameTwo), ConfigurationConstants.ValidationMessages.QdrantCollectionNameTwoValidation);
        var options = databaseOptions.Get<DatabaseOptions>();
        builder.Services.AddSingleton(sp => new QdrantClient(
            options!.HOST, 
            options!.PORT, 
            options!.USE_HTTPS, 
            options!.QDRANT_API_KEY, 
            grpcTimeout: TimeSpan.FromMinutes(30))
        );
        builder.Services.AddSingleton<IQdrantClient, QdrantClientWrapper>();
        builder.Services.AddScoped<IDatabaseWrapper, QdrantDatabaseWrapper>();
        builder.Services.AddScoped<IVectorDbRepository, VectorRepository>();
        return builder;
    }
    
    public static WebApplicationBuilder ConfigureLLMService(this WebApplicationBuilder builder)
    {
        builder.Services.AddSingleton<ILLMServiceLoadBalancer, LLMLoadBalancer>();
        builder.Services.Configure<LLMServiceOptions>(builder.Configuration.GetSection(ConfigurationConstants.LLMServiceOptionsSection));
        builder.Services
            .AddOptions<LLMServiceOptions>()
            .Validate(options => options.LLM_SERVICE_URLS.Count > 0, ConfigurationConstants.ValidationMessages.LLMServiceUrlsValidation)
            .Validate(options => !string.IsNullOrEmpty(options.LLM_SERVICE_URL), ConfigurationConstants.ValidationMessages.LLMServiceUrlValidation);
        builder.Services.AddScoped<ILLMRepository, LLMRepository>();
        return builder;
    }

    public static WebApplicationBuilder ConfigureMediatR(this WebApplicationBuilder builder)
    {
        builder.Services
            .AddFluentValidationAutoValidation()
            .AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(SubmitQuery).Assembly));
        return builder;
    }

    public static WebApplicationBuilder ConfigureServices(this WebApplicationBuilder builder)
    {
        builder.Services.AddMemoryCache();
        builder.Services.AddScoped<IExcelFileService, ExcelFileService>();
        builder.Services.AddScoped<ProgressHub>();
        builder.Services.AddScoped<IProgressHubWrapper, ProgressHubWrapper>();
        builder.Services.AddScoped(typeof(IWebRepository<>), typeof(WebRepository<>));
        return builder;
    }

    public static WebApplicationBuilder ConfigureSwagger(this WebApplicationBuilder builder)
    {
        builder.Services.AddSwaggerGen(c =>
        {
            c.SwaggerDoc(
                ConfigurationConstants.SwaggerConfig.Version, 
                new OpenApiInfo 
                { 
                    Title = ConfigurationConstants.SwaggerConfig.Title, 
                    Version = ConfigurationConstants.SwaggerConfig.Version,
                    Description = ConfigurationConstants.SwaggerConfig.Description,
                });
            c.OperationFilter<SwaggerFileOperationFilter>();
        });
        return builder;
    }

    public static WebApplication ConfigureMiddleware(this WebApplication app)
    {
        if (app.Environment.IsDevelopment()) 
            app.UseSwagger()
               .UseSwaggerUI(
                    options => 
                        options.SwaggerEndpoint(ConfigurationConstants.SwaggerConfig.LaunchUrl, ConfigurationConstants.SwaggerConfig.Version))
               .UseDeveloperExceptionPage(); 

        app.UseMiddleware<ExceptionMiddleware>();
        app.UseWebSockets();
        app.UseHttpsRedirection();
        app.UseStaticFiles();
        app.UseDefaultFiles();
        app.UseRouting();
        app.UseAuthorization();
        app.MapControllers();
        app.MapHealthChecks(ConfigurationConstants.HealthCheckEndpoint);
        return app;
    }

    public static WebApplication ConfigureProgressHub(this WebApplication app)
    {
        app.MapHub<ProgressHub>(ConfigurationConstants.ProgressHubEndpoint);
        return app;
    }

    public static WebApplication ConfigureCors(this WebApplication app)
    {
        app.UseCors(ConfigurationConstants.AppCorsPolicy);
        Array.ForEach(ConfigurationConstants.SupportedUrls, app.Urls.Add);
        return app;
    }

    public static class ConfigurationConstants
    {
        public const string LoggingSection = "Logging";
        public const string AppCorsPolicy = "AllowAll";
        public const string HealthCheckEndpoint = "/health";
        public const string DefaultClientName = "DefaultClient";
        public const string AppSettingsJson = "appsettings.json";
        public const string ProgressHubEndpoint = "/progressHub";
        public const string FrontendUrl = "http://localhost:3000";
        public const string DatabaseOptionsSection = "DatabaseOptions";
        public const string LLMServiceOptionsSection = "LLMServiceOptions";
        public const string AppSettingsEnvironmentJson = "appsettings.{0}.json";
        public static readonly string[] SupportedUrls = ["http://localhost:5001"];
        
        public static class ValidationMessages
        {
            public const string QdrantPortValidation = "Qdrant Port must be set.";
            public const string QdrantApiKeyValidation = "Qdrant API Key must be set.";
            public const string LLMServiceUrlValidation = "LLM_SERVICE_URL must be set.";
            public const string QdrantHostValidation = "Qdrant Host String must be set.";
            public const string LLMServiceUrlsValidation = "LLM_SERVICE_URLS must be set.";
            public const string QdrantBatchSizeValidation = "Qdrant Save Batch Size must be set.";
            public const string QdrantDatabaseNameValidation = "Qdrant Database Name must be set.";
            public const string QdrantCollectionNameValidation = "Qdrant Collection Name must be set.";
            public const string QdrantMaxConnectionValidation = "Qdrant Max Connection Count must be set.";
            public const string QdrantCollectionNameTwoValidation = "Qdrant Collection Name Two must be set.";
        }

        public static class SwaggerConfig
        {
            public const string Version = "v1";
            public const string LaunchUrl = "/swagger/v1/swagger.json";
            public const string Title = "Smart Excel File Analyzer API";
            public const string Description = "API for Smart Excel File Analyzer. Provides interface for uploading and analyzing Excel files (including upload in chunks).";
        }
    }
}