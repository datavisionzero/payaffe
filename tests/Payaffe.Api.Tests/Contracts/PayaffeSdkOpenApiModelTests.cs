using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Payaffe.Sdk;

namespace Payaffe.Api.Tests.Contracts;

/// <summary>
/// The SDK's transport models against the generated OpenAPI document. A field added to the
/// Integration API that the SDK silently drops is the failure this catches: the consumer would
/// read a Payment that is missing data the contract promised, and nothing else would say so.
/// </summary>
public sealed class PayaffeSdkOpenApiModelTests
{
    public static TheoryData<string, Type> ModelPairs => new()
    {
        { "PaymentResponse", typeof(Payment) },
        { "PaymentOptionResponse", typeof(PaymentOption) },
        { "PaymentInstructionResponse", typeof(PaymentInstruction) },
        { "RateLockResponse", typeof(RateLock) },
        { "CreatePaymentHttpRequest", typeof(CreatePaymentRequest) },
        { "PaymentContextHttpRequest", typeof(PaymentContext) },
        { "SimulatedTransactionResponse", typeof(SimulatedTransaction) },
    };

    [Theory]
    [MemberData(nameof(ModelPairs))]
    public async Task Sdk_models_carry_every_field_the_generated_contract_describes(
        string schemaName,
        Type sdkType)
    {
        JsonElement schemas = await ReadSchemasAsync();

        IEnumerable<string> contractProperties = schemas
            .GetProperty(schemaName)
            .GetProperty("properties")
            .EnumerateObject()
            .Select(property => property.Name)
            .Order(StringComparer.Ordinal);

        Assert.Equal(contractProperties, JsonPropertyNamesOf(sdkType));
    }

    [Fact]
    public async Task The_api_exception_exposes_the_problem_fields_a_consumer_can_act_on()
    {
        JsonElement schemas = await ReadSchemasAsync();
        HashSet<string> problemProperties = schemas
            .GetProperty("IntegrationApiProblemResponse")
            .GetProperty("properties")
            .EnumerateObject()
            .Select(property => property.Name)
            .ToHashSet(StringComparer.Ordinal);

        // A subset by design: `type`, `instance`, and `detail` carry nothing a caller branches
        // on, while these four are the contract's answer to "what happened and what now".
        Assert.Subset(
            problemProperties,
            new HashSet<string>(
                ["code", "correlationId", "errors", "status"],
                StringComparer.Ordinal));
    }

    private static async Task<JsonElement> ReadSchemasAsync()
    {
        // The Test Mode document is the live one plus the Test Mode route, so
        // it covers every model the SDK carries.
        await using WebApplicationFactory<Program> factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => builder.UseSetting("Installation:Mode", "test"));
        using HttpClient client = factory.CreateClient();
        using HttpResponseMessage response = await client.GetAsync("/openapi/v1.json");
        response.EnsureSuccessStatusCode();

        JsonDocument document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("components").GetProperty("schemas").Clone();
    }

    private static IEnumerable<string> JsonPropertyNamesOf(Type type) => type
        .GetProperties(BindingFlags.Public | BindingFlags.Instance)
        .Select(property => JsonNamingPolicy.CamelCase.ConvertName(property.Name))
        .Order(StringComparer.Ordinal);
}
