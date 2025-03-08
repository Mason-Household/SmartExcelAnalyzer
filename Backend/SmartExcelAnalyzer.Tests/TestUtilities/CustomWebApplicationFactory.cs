using API;
using Persistence.Hubs;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace SmartExcelAnalyzer.Tests.TestUtilities;

class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.ConfigureWebHost(webHost =>
        {
            webHost.UseTestServer(options => options.PreserveExecutionContext = true);
            webHost.ConfigureKestrel(options => options.ListenLocalhost(0));
            webHost.UseEnvironment("Development");
            webHost.UseStartup<Program>();
            webHost.Configure(app =>
            {
                app.UseRouting();
                app.UseCors(policy => 
                    policy.AllowAnyOrigin()
                          .AllowAnyMethod()
                          .AllowAnyHeader());
                app.UseEndpoints(endpoints =>
                {
                    endpoints.MapControllers();
                    endpoints.MapHealthChecks("/health");
                    endpoints.MapHub<ProgressHub>("/progressHub");
                });
            });
        });
        return base.CreateHost(builder);
    }
}
