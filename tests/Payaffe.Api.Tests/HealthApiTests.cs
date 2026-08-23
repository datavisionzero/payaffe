using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Payaffe.Api.Tests;

public sealed class HealthApiTests
{
    [Fact]
    public async Task Live_health_does_not_require_database_connectivity()
    {
        await using var factory = CreateFactoryWithUnavailableDatabase();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/live");

        response.EnsureSuccessStatusCode();
        Assert.Equal("""{"status":"alive"}""", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Ready_health_fails_without_database_connectivity_and_does_not_expose_configuration()
    {
        await using var factory = CreateFactoryWithUnavailableDatabase();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/ready");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("""{"status":"not_ready"}""", body);
        Assert.DoesNotContain("Password", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("payaffe-health-tests", body, StringComparison.OrdinalIgnoreCase);
    }

    private static WebApplicationFactory<Program> CreateFactoryWithUnavailableDatabase()
    {
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((_, configuration) =>
                {
                    configuration.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["ConnectionStrings:Payaffe"] =
                            "Host=127.0.0.1;Port=1;Database=payaffe-health-tests;Username=payaffe;Password=not-secret;Timeout=1;Command Timeout=1",
                    });
                });
            });
    }
}
