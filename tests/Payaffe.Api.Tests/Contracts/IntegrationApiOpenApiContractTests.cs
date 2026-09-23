using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Payaffe.Api.Tests.Contracts;

public sealed class IntegrationApiOpenApiContractTests
{
    private const string SnapshotPathFromRepositoryRoot = "docs/contracts/integration-api/openapi.v1.json";

    /// <summary>
    /// The same v1 document as a Test Mode installation serves it: the live
    /// contract plus the routes that exist only there (ADR 0033). Kept apart
    /// so the live snapshot stays exactly what a production integration can
    /// call.
    /// </summary>
    private const string TestModeSnapshotPathFromRepositoryRoot = "docs/contracts/integration-api/openapi.v1.test-mode.json";

    private const string SimulatedTransactionsPath = "/api/v1/payments/{paymentId}/simulated-transactions";

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
    public async Task Test_mode_OpenApi_document_matches_its_accepted_contract_snapshot()
    {
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.UseSetting("Installation:Mode", "test"));
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/openapi/v1.json");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var generated = NormalizeJson(await response.Content.ReadAsStringAsync());
        var snapshotPath = Path.Combine(FindRepositoryRoot(), TestModeSnapshotPathFromRepositoryRoot);
        if (ShouldUpdateSnapshot())
        {
            await File.WriteAllTextAsync(snapshotPath, generated);
        }

        Assert.True(File.Exists(snapshotPath), $"Accepted OpenAPI snapshot is missing at {snapshotPath}.");
        Assert.Equal(NormalizeJson(await File.ReadAllTextAsync(snapshotPath)), generated);

        using var document = JsonDocument.Parse(generated);
        var simulate = document.RootElement.GetProperty("paths").GetProperty(SimulatedTransactionsPath).GetProperty("post");
        Assert.Equal("RecordSimulatedTransaction", simulate.GetProperty("operationId").GetString());
        AssertHasBearerSecurity(simulate);
        AssertHasResponse(simulate, "201");
        AssertHasResponse(simulate, "404");
        AssertHasResponse(simulate, "409");
    }

    [Fact]
    public async Task The_live_OpenApi_document_has_no_test_mode_route()
    {
        await using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();

        using var document = JsonDocument.Parse(await client.GetStringAsync("/openapi/v1.json"));

        Assert.False(document.RootElement.GetProperty("paths").TryGetProperty(SimulatedTransactionsPath, out _));
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
        Assert.True(paths.TryGetProperty(
            "/api/v1/payments/{paymentId}/currency-selection",
            out var currencySelectionPath));

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

        var selectCurrency = currencySelectionPath.GetProperty("put");
        Assert.Equal("SelectPaymentCurrency", selectCurrency.GetProperty("operationId").GetString());
        AssertHasBearerSecurity(selectCurrency);
        Assert.True(selectCurrency.GetProperty("requestBody").GetProperty("required").GetBoolean());
        AssertHasResponse(selectCurrency, "200");
        AssertHasResponse(selectCurrency, "400");
        AssertHasResponse(selectCurrency, "401");
        AssertHasResponse(selectCurrency, "404");
        AssertHasResponse(selectCurrency, "409");
        AssertHasResponse(selectCurrency, "429");

        var schemas = root.GetProperty("components").GetProperty("schemas");
        var problem = schemas.GetProperty("IntegrationApiProblemResponse");
        var problemRequiredProperties = problem.GetProperty("required").EnumerateArray().Select(value => value.GetString());
        Assert.Contains("code", problemRequiredProperties);
        Assert.Contains("correlationId", problemRequiredProperties);
        Assert.True(problem.GetProperty("properties").TryGetProperty("errors", out _));

        var paymentResponse = schemas.GetProperty("PaymentResponse");
        var paymentProperties = paymentResponse.GetProperty("properties");
        Assert.True(paymentProperties.TryGetProperty("createdAt", out _));
        Assert.True(paymentProperties.TryGetProperty("updatedAt", out _));
        Assert.True(paymentProperties.TryGetProperty("lateAcceptanceEndsAt", out _));
        Assert.True(paymentProperties.TryGetProperty("confirmedEligibleTotal", out _));
        Assert.True(paymentProperties.TryGetProperty("observedAmountState", out _));
        Assert.True(paymentProperties.TryGetProperty("rateLock", out _));
        Assert.True(paymentProperties.TryGetProperty("paymentInstruction", out _));

        var rateLock = schemas.GetProperty("RateLockResponse").GetProperty("properties");
        Assert.True(rateLock.TryGetProperty("expectedCryptoAmountAtomic", out _));
        Assert.True(rateLock.TryGetProperty("validUntil", out _));

        var instruction = schemas.GetProperty("PaymentInstructionResponse").GetProperty("properties");
        Assert.True(instruction.TryGetProperty("network", out _));
        Assert.True(instruction.TryGetProperty("chainId", out _));
        Assert.True(instruction.TryGetProperty("amountAtomic", out _));
        Assert.True(instruction.TryGetProperty("uri", out _));
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
