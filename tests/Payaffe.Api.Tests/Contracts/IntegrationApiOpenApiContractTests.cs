using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Payaffe.Api.Tests.Contracts;

public sealed class IntegrationApiOpenApiContractTests
{
    private const string SnapshotPathFromRepositoryRoot = "docs/contracts/integration-api/openapi.v1.json";

    [Fact]
    public async Task OpenApi_document_matches_accepted_contract_snapshot()
    {
        await using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/openapi/v1.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var generated = NormalizeJson(await response.Content.ReadAsStringAsync());
        var snapshotPath = Path.Combine(FindRepositoryRoot(), SnapshotPathFromRepositoryRoot);
        if (ShouldUpdateSnapshot())
        {
            Directory.CreateDirectory(Path.GetDirectoryName(snapshotPath)!);
            await File.WriteAllTextAsync(snapshotPath, generated);
        }

        Assert.True(
            File.Exists(snapshotPath),
            $"Accepted OpenAPI snapshot is missing at {snapshotPath}.");
        var accepted = NormalizeJson(await File.ReadAllTextAsync(snapshotPath));
        Assert.Equal(accepted, generated);
    }

    [Fact]
    public async Task OpenApi_document_describes_required_integration_api_contract_elements()
    {
        await using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/openapi/v1.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = document.RootElement;
        Assert.Equal("payaffe Integration API", root.GetProperty("info").GetProperty("title").GetString());
        Assert.Equal("v1", root.GetProperty("info").GetProperty("version").GetString());

        var paths = root.GetProperty("paths");
        Assert.DoesNotContain(paths.EnumerateObject(), path => path.Name.StartsWith("/health/", StringComparison.Ordinal));
        Assert.True(paths.TryGetProperty("/api/v1/payments", out var paymentsPath));
        Assert.True(paths.TryGetProperty("/api/v1/payments/{paymentId}", out var paymentByIdPath));

        var createPayment = paymentsPath.GetProperty("post");
        Assert.Equal("CreatePayment", createPayment.GetProperty("operationId").GetString());
        AssertHasBearerSecurity(createPayment);
        AssertHasResponse(createPayment, "201");
        AssertHasResponse(createPayment, "200");
        AssertHasResponse(createPayment, "400");
        AssertHasResponse(createPayment, "401");
        AssertHasResponse(createPayment, "409");
        AssertHasResponse(createPayment, "429");
        AssertHasRequiredHeaderParameter(createPayment, "Idempotency-Key");
        Assert.True(createPayment.GetProperty("requestBody").GetProperty("required").GetBoolean());

        var getPayment = paymentByIdPath.GetProperty("get");
        Assert.Equal("GetPayment", getPayment.GetProperty("operationId").GetString());
        AssertHasBearerSecurity(getPayment);
        AssertHasResponse(getPayment, "200");
        AssertHasResponse(getPayment, "401");
        AssertHasResponse(getPayment, "404");
        AssertHasResponse(getPayment, "429");

        var schemas = root.GetProperty("components").GetProperty("schemas");
        var problem = schemas.GetProperty("IntegrationApiProblemResponse");
        var problemRequiredProperties = problem.GetProperty("required").EnumerateArray().Select(value => value.GetString());
        Assert.Contains("code", problemRequiredProperties);
        Assert.Contains("correlationId", problemRequiredProperties);
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

    private static void AssertHasBearerSecurity(JsonElement operation)
    {
        var security = operation.GetProperty("security");
        Assert.Contains(
            security.EnumerateArray(),
            requirement => requirement.TryGetProperty("Bearer", out _));
    }

    private static void AssertHasResponse(JsonElement operation, string statusCode)
    {
        Assert.True(operation.GetProperty("responses").TryGetProperty(statusCode, out _));
    }

    private static void AssertHasRequiredHeaderParameter(JsonElement operation, string parameterName)
    {
        var parameters = operation.GetProperty("parameters").EnumerateArray();
        Assert.Contains(
            parameters,
            parameter =>
                parameter.GetProperty("name").GetString() == parameterName &&
                parameter.GetProperty("in").GetString() == "header" &&
                parameter.GetProperty("required").GetBoolean());
    }
}
