using API.Middleware;
using Application.Queries;
using Application.Services;
using Persistence.Repositories;
using FluentValidation.AspNetCore;
using Domain.Persistence.Configuration;
using Persistence.Database;
using MongoDB.Driver;
using Microsoft.OpenApi.Models;
using API.Properties;
using Persistence.Cache;

namespace API.Extensions;

public static class ConfigurationExtensions
{
    public static WebApplicationBuilder ConfigureEnvironmentVariables(this WebApplicationBuilder? builder)
    {
        builder ??= WebApplication.CreateBuilder();
        builder!.Configuration.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
        builder.Configuration.AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true);
        builder.Configuration.AddEnvironmentVariables();
        return builder;
    }

    public static WebApplicationBuilder ConfigureLogging(this WebApplicationBuilder builder)
    {
        builder.Logging.ClearProviders();
        builder.Logging.AddConfiguration(builder.Configuration.GetSection("Logging"));
        builder.Logging.AddConsole();
        builder.Services.AddLogging();
        return builder;
    }

    public static WebApplicationBuilder ConfigureHttpClient(this WebApplicationBuilder builder)
    {
        builder.Services.AddHttpClient("DefaultClient", client => 
        { 
            client.Timeout = TimeSpan.FromMinutes(20);
        });
        return builder;
    }

    public static WebApplicationBuilder ConfigureApiAccess(this WebApplicationBuilder builder)
    {
        builder.Services.AddControllers();
        builder.Services.AddHealthChecks();
        builder.Services.AddCors(options =>
        {
            options.AddDefaultPolicy(builder => 
            {
                builder.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader();
            });
        });
        return builder;
    }

    public static WebApplicationBuilder ConfigureDatabase(this WebApplicationBuilder builder)
    {
        builder.Services.Configure<DatabaseOptions>(builder.Configuration.GetSection("DatabaseOptions"));
        builder.Services.AddOptions<DatabaseOptions>()
            .Validate(options => !string.IsNullOrEmpty(options.ConnectionString), "MongoDB ConnectionString must be set.");
        
        var databaseOptions = builder.Configuration.GetSection("DatabaseOptions").Get<DatabaseOptions>();
        builder.Services.AddSingleton<IMongoClient>(sp => new MongoClient(databaseOptions!.ConnectionString));
        builder.Services.AddScoped(sp =>
        {
            var client = sp.GetRequiredService<IMongoClient>();
            return client.GetDatabase(databaseOptions!.DatabaseName);
        });
        builder.Services.AddScoped<IDatabaseWrapper, MongoDatabaseWrapper>();
        builder.Services.AddScoped<IVectorDbRepository, VectorRepository>();
        builder.Services.AddMemoryCache(options =>
        {
            options.SizeLimit = 1_000_000;
        });
        builder.Services.AddSingleton<IEmbeddingCache, MemoryCacheEmbeddingCache>();
        return builder;
    }

    public static WebApplicationBuilder ConfigureLLMService(this WebApplicationBuilder builder)
    {
        builder.Services.Configure<LLMServiceOptions>(builder.Configuration.GetSection("LLMServiceOptions"));
        builder.Services.AddOptions<LLMServiceOptions>()
            .Validate(options => !string.IsNullOrEmpty(options.LLM_SERVICE_URL), "LLM_SERVICE_URL must be set.");
        builder.Services.AddScoped<ILLMRepository, LLMRepository>();
        return builder;
    }

    public static WebApplicationBuilder ConfigureMediatR(this WebApplicationBuilder builder)
    {
        builder.Services.AddFluentValidationAutoValidation();
        builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(SubmitQuery).Assembly));
        return builder;
    }

    public static WebApplicationBuilder ConfigureServices(this WebApplicationBuilder builder)
    {
        builder.Services.AddScoped<IExcelFileService, ExcelFileService>();
        builder.Services.AddScoped(typeof(IWebRepository<>), typeof(WebRepository<>));
        return builder;
    }

    public static WebApplicationBuilder ConfigureSwagger(this WebApplicationBuilder builder)
    {
        builder.Services.AddSwaggerGen(c =>
        {
            c.SwaggerDoc("v1", new OpenApiInfo { Title = "Smart Excel File Analyzer API", Version = "v1" });
            c.OperationFilter<SwaggerFileOperationFilter>();
        });
        return builder;
    }

    public static WebApplication ConfigureMiddleware(this WebApplication app)
    {
        if (app.Environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI();
            app.UseDeveloperExceptionPage();
        }
        app.UseCors();
        app.UseRouting();
        app.MapControllers();
        app.MapHealthChecks("/health");
        app.UseMiddleware<ExceptionMiddleware>();
        return app;
    }
}