using System.Diagnostics.CodeAnalysis;
using static API.Extensions.ProgramExtensions;

namespace API;

[ExcludeFromCodeCoverage]
public class Program
{
   public static void Main(string[] args) => 
       ConfigureSmartExcelAnalyzerProgram(args)
       .Run();
}

// Add this to your Program.cs file to replace your current configuration

// Add this to your Program.cs file to replace your current configuration


// using Persistence.Hubs;
// using Microsoft.AspNetCore.Builder;
// using Microsoft.Extensions.DependencyInjection;

// var builder = WebApplication.CreateBuilder(args);

// // Only add essential services
// builder.Services.AddSignalR();
// builder.Services.AddCors(options =>
// {
//     options.AddPolicy("CorsPolicy", builder =>
//     {
//         builder.SetIsOriginAllowed(_ => true)
//                .AllowAnyMethod()
//                .AllowAnyHeader()
//                .AllowCredentials();
//     });
// });
// builder.Services.AddControllers();

// // Do NOT configure Kestrel explicitly - use only environment variable
// // ASPNETCORE_URLS=http://+:5000

// var app = builder.Build();

// app.UseCors("CorsPolicy");
// app.UseRouting();
// app.UseWebSockets();

// app.MapHub<ProgressHub>("/progressHub");
// app.MapControllers();
// app.MapGet("/", () => "Hello World!");

// app.Run();