using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Payaffe.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Payaffe.Api.Tests.Payments;

public sealed class PaymentApiTests
{
    private const string ValidToken = "valid-test-token";

    [Fact]
    public async Task Get_payment_requires_authentication()
    {
        await using var factory = new PaymentApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync($"/api/v1/payments/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        await AssertProblemCodeAsync(response, "authentication.required");
    }

    [Fact]
    public async Task Get_payment_rejects_invalid_token()
    {
        await using var factory = new PaymentApiFactory();
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "wrong-token");

        var response = await client.GetAsync($"/api/v1/payments/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        await AssertProblemCodeAsync(response, "authentication.invalid");
    }

    [Fact]
    public async Task Get_payment_rate_limit_returns_contract_problem_and_audits_denial()
    {
        await using var factory = new PaymentApiFactory
        {
            IntegrationApiRateLimitPermitLimit = 1,
            IntegrationApiRateLimitWindow = TimeSpan.FromMinutes(1),
        };
        await factory.SeedCredentialAsync(ValidToken);
        using var client = CreateAuthenticatedClient(factory);
        var paymentId = Guid.NewGuid();
        var firstResponse = await client.GetAsync($"/api/v1/payments/{paymentId}");
        Assert.Equal(HttpStatusCode.NotFound, firstResponse.StatusCode);

        var response = await client.GetAsync($"/api/v1/payments/{paymentId}");

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        using var problem = await ReadProblemAsync(response);
        Assert.Equal("rate_limited", problem.RootElement.GetProperty("code").GetString());
        var body = problem.RootElement.GetRawText();
        Assert.DoesNotContain(ValidToken, body, StringComparison.Ordinal);

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PayaffeDbContext>();
        Assert.Contains(dbContext.AuditLogEntries, entry =>
            entry.EventType == "integration_api.rate_limit" &&
            entry.Outcome == "denied" &&
            entry.ActorType == "system" &&
            entry.ActorId == "unknown" &&
            entry.SubjectType == "integration_api_credential" &&
            entry.SubjectId == "unknown" &&
            entry.ReasonCode == "rate_limited");
    }

    [Fact]
    public async Task Create_payment_returns_validation_problem_details()
    {
        await using var factory = new PaymentApiFactory();
        await factory.SeedCredentialAsync(ValidToken);
        using var client = CreateAuthenticatedClient(factory);

        var response = await client.PostAsJsonAsync(
            "/api/v1/payments",
            new
            {
                fiatCurrency = "CHF",
                fiatAmountMinor = 0,
                externalReference = "",
                paymentContext = new
                {
                    username = new string('x', 256),
                },
                returnUrl = "relative/path",
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var problem = await ReadProblemAsync(response);
        Assert.Equal("validation.failed", problem.RootElement.GetProperty("code").GetString());
        var errors = problem.RootElement.GetProperty("errors");
        Assert.Equal("idempotency_key.required", errors.GetProperty("headers.idempotencyKey")[0].GetString());
        Assert.Equal("fiat_currency.unsupported", errors.GetProperty("fiatCurrency")[0].GetString());
        Assert.Equal("fiat_amount.not_positive", errors.GetProperty("fiatAmountMinor")[0].GetString());
        Assert.Equal("external_reference.required", errors.GetProperty("externalReference")[0].GetString());
        Assert.Equal("payment_context.username.too_long", errors.GetProperty("paymentContext.username")[0].GetString());
        Assert.Equal("return_url.invalid", errors.GetProperty("returnUrl")[0].GetString());
    }

    [Fact]
    public async Task Create_payment_creates_pending_currency_selection_payment()
    {
        await using var factory = new PaymentApiFactory();
        await factory.SeedCredentialAsync(ValidToken);
        using var client = CreateAuthenticatedClient(factory);
        client.DefaultRequestHeaders.Add("Idempotency-Key", "create-order-123");

        var response = await client.PostAsJsonAsync("/api/v1/payments", ValidCreatePaymentRequest());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var payment = await response.Content.ReadFromJsonAsync<PaymentApiResponse>();
        Assert.NotNull(payment);
        Assert.NotEqual(Guid.Empty, payment.PaymentId);
        Assert.Equal("pending_currency_selection", payment.Status);
        Assert.StartsWith("https://pay.example.test/pay/", payment.PayerPageUrl, StringComparison.Ordinal);
        Assert.Equal("EUR", payment.FiatCurrency);
        Assert.Equal(1999, payment.FiatAmountMinor);
        Assert.Equal("order-123", payment.ExternalReference);
        Assert.Equal("https://example.test/orders/order-123", payment.ReturnUrl);
    }

    [Fact]
    public async Task Create_payment_reuses_existing_payment_for_same_idempotency_key_and_request()
    {
        await using var factory = new PaymentApiFactory();
        await factory.SeedCredentialAsync(ValidToken);
        using var client = CreateAuthenticatedClient(factory);
        client.DefaultRequestHeaders.Add("Idempotency-Key", "same-request");

        var firstResponse = await client.PostAsJsonAsync("/api/v1/payments", ValidCreatePaymentRequest());
        var secondResponse = await client.PostAsJsonAsync("/api/v1/payments", ValidCreatePaymentRequest());

        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
        var firstPayment = await firstResponse.Content.ReadFromJsonAsync<PaymentApiResponse>();
        var secondPayment = await secondResponse.Content.ReadFromJsonAsync<PaymentApiResponse>();
        Assert.Equal(firstPayment!.PaymentId, secondPayment!.PaymentId);
        Assert.Equal(firstPayment.PayerPageUrl, secondPayment.PayerPageUrl);
    }

    [Fact]
    public async Task Create_payment_returns_conflict_for_same_idempotency_key_with_different_request()
    {
        await using var factory = new PaymentApiFactory();
        await factory.SeedCredentialAsync(ValidToken);
        using var client = CreateAuthenticatedClient(factory);
        client.DefaultRequestHeaders.Add("Idempotency-Key", "conflicting-request");

        var firstResponse = await client.PostAsJsonAsync("/api/v1/payments", ValidCreatePaymentRequest());
        var conflictResponse = await client.PostAsJsonAsync(
            "/api/v1/payments",
            ValidCreatePaymentRequest() with { FiatAmountMinor = 2999 });

        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, conflictResponse.StatusCode);
        await AssertProblemCodeAsync(conflictResponse, "idempotency.conflict");
    }

    [Fact]
    public async Task Get_payment_returns_created_payment()
    {
        await using var factory = new PaymentApiFactory();
        await factory.SeedCredentialAsync(ValidToken);
        using var client = CreateAuthenticatedClient(factory);
        client.DefaultRequestHeaders.Add("Idempotency-Key", "get-created-payment");
        var createResponse = await client.PostAsJsonAsync("/api/v1/payments", ValidCreatePaymentRequest());
        var createdPayment = await createResponse.Content.ReadFromJsonAsync<PaymentApiResponse>();

        var response = await client.GetAsync($"/api/v1/payments/{createdPayment!.PaymentId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payment = await response.Content.ReadFromJsonAsync<PaymentApiResponse>();
        Assert.Equal(createdPayment.PaymentId, payment!.PaymentId);
        Assert.Equal("pending_currency_selection", payment.Status);
        Assert.Null(payment.SelectedCurrency);
        Assert.Null(payment.ExpectedCryptoAmount);
        Assert.Null(payment.PaymentAddress);
        Assert.Null(payment.ObservedTotal);
        Assert.Null(payment.CompletedAt);
        Assert.Null(payment.SettledAt);
    }

    [Fact]
    public async Task Get_payment_returns_not_found_problem_for_unknown_payment()
    {
        await using var factory = new PaymentApiFactory();
        await factory.SeedCredentialAsync(ValidToken);
        using var client = CreateAuthenticatedClient(factory);

        var response = await client.GetAsync($"/api/v1/payments/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await AssertProblemCodeAsync(response, "payment.not_found");
    }

    [Fact]
    public async Task Payer_payment_can_be_read_without_integration_api_authentication()
    {
        await using var factory = new PaymentApiFactory();
        await factory.SeedCredentialAsync(ValidToken);
        using var client = CreateAuthenticatedClient(factory);
        client.DefaultRequestHeaders.Add("Idempotency-Key", "payer-read-payment");
        var createResponse = await client.PostAsJsonAsync("/api/v1/payments", ValidCreatePaymentRequest());
        var createdPayment = await createResponse.Content.ReadFromJsonAsync<PaymentApiResponse>();
        using var publicClient = factory.CreateClient();

        var response = await publicClient.GetAsync($"/api/payer/payments/{ExtractPayerPageId(createdPayment!.PayerPageUrl)}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payment = await response.Content.ReadFromJsonAsync<PaymentApiResponse>();
        Assert.Equal(createdPayment.PaymentId, payment!.PaymentId);
        Assert.Equal("pending_currency_selection", payment.Status);
    }

    [Fact]
    public async Task Payer_currency_selection_returns_payment_instruction()
    {
        await using var factory = new PaymentApiFactory();
        await factory.SeedCredentialAsync(ValidToken);
        using var client = CreateAuthenticatedClient(factory);
        client.DefaultRequestHeaders.Add("Idempotency-Key", "payer-select-currency");
        var createResponse = await client.PostAsJsonAsync("/api/v1/payments", ValidCreatePaymentRequest());
        var createdPayment = await createResponse.Content.ReadFromJsonAsync<PaymentApiResponse>();
        using var publicClient = factory.CreateClient();

        var response = await publicClient.PostAsJsonAsync(
            $"/api/payer/payments/{ExtractPayerPageId(createdPayment!.PayerPageUrl)}/currency-selection",
            new
            {
                supportedCurrency = "btc",
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payment = await response.Content.ReadFromJsonAsync<PaymentApiResponse>();
        Assert.Equal(createdPayment.PaymentId, payment!.PaymentId);
        Assert.Equal("waiting_for_payment", payment.Status);
        Assert.Equal("BTC", payment.SelectedCurrency);
        Assert.Equal("0.00039980", payment.ExpectedCryptoAmount);
        Assert.Equal("btc-test-address", payment.PaymentAddress);
    }

    private static HttpClient CreateAuthenticatedClient(PaymentApiFactory factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ValidToken);
        return client;
    }

    private static CreatePaymentRequest ValidCreatePaymentRequest()
    {
        return new CreatePaymentRequest(
            "EUR",
            1999,
            "order-123",
            new PaymentContextRequest(
                "customer@example.test",
                "C-1000",
                "Starter package",
                "Optional admin-facing note"),
            "https://example.test/orders/order-123");
    }

    private static string ExtractPayerPageId(string payerPageUrl)
    {
        var uri = new Uri(payerPageUrl);
        return uri.Segments.Last().TrimEnd('/');
    }

    private static async Task AssertProblemCodeAsync(HttpResponseMessage response, string expectedCode)
    {
        using var problem = await ReadProblemAsync(response);
        Assert.Equal(expectedCode, problem.RootElement.GetProperty("code").GetString());
        Assert.True(problem.RootElement.TryGetProperty("correlationId", out _));
    }

    private static async Task<JsonDocument> ReadProblemAsync(HttpResponseMessage response)
    {
        var content = await response.Content.ReadAsStringAsync();
        return JsonDocument.Parse(content);
    }

    private sealed record CreatePaymentRequest(
        string FiatCurrency,
        long FiatAmountMinor,
        string ExternalReference,
        PaymentContextRequest PaymentContext,
        string ReturnUrl);

    private sealed record PaymentContextRequest(
        string Username,
        string CustomerNumber,
        string CartName,
        string Note);

    private sealed record PaymentApiResponse(
        Guid PaymentId,
        string Status,
        string PayerPageUrl,
        DateTimeOffset ExpiresAt,
        string FiatCurrency,
        long FiatAmountMinor,
        string ExternalReference,
        string? SelectedCurrency,
        string? ExpectedCryptoAmount,
        string? PaymentAddress,
        string? ObservedTotal,
        DateTimeOffset? CompletedAt,
        DateTimeOffset? SettledAt,
        string? ReturnUrl);
}
