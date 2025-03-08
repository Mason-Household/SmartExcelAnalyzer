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
        .ConfigureMiddleware();
    
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
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole();
        builder.Logging.AddDebug();
        builder.Logging.SetMinimumLevel(LogLevel.Information);
        
        builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Information);
        
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
        builder.Services.AddSignalR(options => {
            options.EnableDetailedErrors = true;
        });
        builder.Services.AddHealthChecks();
        builder.Services.AddControllers(options => options.AddCommonResponseTypes());
        builder.Services.AddCors(options =>
        {
            options.AddPolicy("CorsPolicy", builder =>
            {
                builder
                    .AllowAnyMethod()
                    .AllowAnyHeader()
                    .SetIsOriginAllowed(_ => true)
                    .AllowCredentials();
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
        app.UseSwagger()
        .UseSwaggerUI(options => 
        {
            //options.SwaggerEndpoint("./swagger/v1/swagger.json", ConfigurationConstants.SwaggerConfig.Version);
            options.RoutePrefix = "swagger";
        });

        if (app.Environment.IsDevelopment())
        {
            app.UseDeveloperExceptionPage();
        }
        app.UseCors("CorsPolicy");
    
        // Then routing
        app.UseRouting();
        
        // Configure WebSockets with proper options
        app.UseWebSockets(new WebSocketOptions
        {
            KeepAliveInterval = TimeSpan.FromSeconds(30),
            AllowedOrigins = { "http://localhost:3000" }
        });
        
        app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments("/progressHub"))
            {
                var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
                logger.LogInformation("SignalR request received: {Path} {Method} {QueryString}",
                    context.Request.Path,
                    context.Request.Method,
                    context.Request.QueryString);
                    
                foreach (var header in context.Request.Headers)
                {
                    logger.LogInformation("Header: {Key}={Value}", header.Key, header.Value);
                }
            }
            
            await next();
        });
        
        // Other middleware...
        // app.UseHttpsRedirection();
        app.UseStaticFiles();
        app.UseDefaultFiles();
        //app.UseAuthorization();
        
        // Map controllers and health checks
        app.MapControllers();
        app.MapHealthChecks(ConfigurationConstants.HealthCheckEndpoint);
        
        // Map hub endpoint - make sure this comes after all other middleware
        app.MapHub<ProgressHub>("/progressHub");
        
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
        public static readonly string[] SupportedUrls = [
            "http://localhost:5000", 
            "https://localhost:44359", 
            "http://localhost:5000",
            "http://localhost:3000" 
        ];
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