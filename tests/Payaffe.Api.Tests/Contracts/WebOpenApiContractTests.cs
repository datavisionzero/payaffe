using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Payaffe.Api.Tests.Contracts;

public sealed class WebOpenApiContractTests
{
    private const string SnapshotPathFromRepositoryRoot = "docs/contracts/web/openapi.json";

    [Fact]
    public async Task Web_OpenApi_document_matches_accepted_contract_snapshot()
    {
        await using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/openapi/web.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var generated = NormalizeJson(await response.Content.ReadAsStringAsync());
        var snapshotPath = Path.Combine(FindRepositoryRoot(), SnapshotPathFromRepositoryRoot);
        if (ShouldUpdateSnapshot())
        {
            Directory.CreateDirectory(Path.GetDirectoryName(snapshotPath)!);
            await File.WriteAllTextAsync(snapshotPath, generated);
        }

        Assert.True(File.Exists(snapshotPath), $"Accepted Web OpenAPI snapshot is missing at {snapshotPath}.");
        var accepted = NormalizeJson(await File.ReadAllTextAsync(snapshotPath));
        Assert.Equal(accepted, generated);
    }

    [Fact]
    public async Task Web_OpenApi_document_describes_payer_and_admin_contracts()
    {
        await using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/openapi/web.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        Assert.Equal("payaffe Web API", root.GetProperty("info").GetProperty("title").GetString());
        Assert.Equal("web", root.GetProperty("info").GetProperty("version").GetString());

        var paths = root.GetProperty("paths");
        Assert.DoesNotContain(
            paths.EnumerateObject(),
            path => path.Name.StartsWith("/api/v1/", StringComparison.Ordinal));
        Assert.True(paths.TryGetProperty("/api/payer/payments/{payerPageId}", out _));
        Assert.True(paths.TryGetProperty("/api/admin/payments/{paymentId}/settle", out _));
        Assert.True(paths.TryGetProperty("/api/admin/integration-api-credentials", out _));
        Assert.True(paths.TryGetProperty("/api/admin/webhook-endpoints", out _));
        Assert.True(paths.TryGetProperty("/api/admin/native-eth-address-pool/import", out _));
        Assert.True(paths.TryGetProperty("/api/admin/observation-health", out _));
        Assert.True(paths.TryGetProperty("/api/admin/reorg-alerts", out _));

        var schemas = root.GetProperty("components").GetProperty("schemas");
        Assert.True(schemas.TryGetProperty("PaymentResponse", out _));
        Assert.True(schemas.TryGetProperty("AdminPaymentDetailReadModel", out _));
        Assert.True(schemas.TryGetProperty("AdminIntegrationApiCredentialReadModel", out _));
        Assert.True(schemas.TryGetProperty("AdminWebhookEndpointReadModel", out _));
        Assert.True(schemas.TryGetProperty("AdminNativeEthAddressPoolSummary", out _));
        Assert.True(schemas.TryGetProperty("ObservationHealthReadModel", out _));
        Assert.True(schemas.TryGetProperty("AdminReorgAlertReadModel", out _));
        Assert.True(schemas.TryGetProperty("IntegrationApiProblemResponse", out var problem));
        Assert.True(problem.GetProperty("properties").TryGetProperty("errors", out _));
    }

    private static bool ShouldUpdateSnapshot()
    {
        return string.Equals(
            Environment.GetEnvironmentVariable("PAYAFFE_UPDATE_CONTRACT_SNAPSHOTS"),
            "true",
            StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeJson(string json)
    {
        using var document = JsonDocument.Parse(json);
        return JsonSerializer.Serialize(
            document.RootElement,
            new JsonSerializerOptions
            {
                WriteIndented = true,
            }) + Environment.NewLine;
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Payaffe.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory.FullName;
    }
}
