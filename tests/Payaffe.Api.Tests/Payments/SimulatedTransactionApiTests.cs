using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Payaffe.Application.Payments;
using Payaffe.Sdk;

namespace Payaffe.Api.Tests.Payments;

/// <summary>
/// The Test Mode route an embedded integration simulates a payment through
/// (ADR 0033).
/// </summary>
public sealed class SimulatedTransactionApiTests
{
    private const string Token = "simulation-token";

    [Fact]
    public async Task A_simulated_payment_runs_the_payment_to_completion()
    {
        await using var factory = new PaymentApiFactory { TestMode = true };
        await factory.SeedCredentialAsync(Token);
        using var client = CreateClient(factory);
        var payment = await CreateSelectedPaymentAsync(client);

        using var response = await client.PostAsync(SimulatePath(payment.PaymentId), content: null);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(payment.ExpectedCryptoAmount, body.RootElement.GetProperty("amount").GetString());
        Assert.Equal(payment.PaymentAddress, body.RootElement.GetProperty("paymentAddress").GetString());
        Assert.Equal(64, body.RootElement.GetProperty("transactionHash").GetString()!.Length);

        await PollAsync(factory);
        Assert.Equal(PaymentStatus.Observed, (await GetAsync(client, payment.PaymentId)).Status);
        await PollAsync(factory);
        Assert.Equal(PaymentStatus.Completed, (await GetAsync(client, payment.PaymentId)).Status);
    }

    [Fact]
    public async Task A_retry_with_the_same_idempotency_key_records_nothing_new()
    {
        await using var factory = new PaymentApiFactory { TestMode = true };
        await factory.SeedCredentialAsync(Token);
        using var client = CreateClient(factory);
        var payment = await CreateSelectedPaymentAsync(client);

        using var first = await SimulateAsync(client, payment.PaymentId, "0.0001", "attempt-1");
        using var retry = await SimulateAsync(client, payment.PaymentId, "0.0001", "attempt-1");
        using var conflicting = await SimulateAsync(client, payment.PaymentId, "0.0002", "attempt-1");

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        Assert.Equal(
            await TransactionHashAsync(first),
            await TransactionHashAsync(retry));
        Assert.Equal(HttpStatusCode.Conflict, conflicting.StatusCode);
        Assert.Equal("idempotency.conflict", await ProblemCodeAsync(conflicting));
    }

    [Fact]
    public async Task A_payment_without_a_selected_currency_cannot_be_paid_yet()
    {
        await using var factory = new PaymentApiFactory { TestMode = true };
        await factory.SeedCredentialAsync(Token);
        using var client = CreateClient(factory);
        var payaffe = new PayaffeClient(client, new Uri("http://localhost"), Token);
        var created = await payaffe.CreatePaymentAsync(new CreatePaymentRequest("EUR", 1999, "unselected"), "unselected-1");

        using var response = await client.PostAsync(SimulatePath(created.PaymentId), content: null);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("payment.not_waiting_for_payment", await ProblemCodeAsync(response));
    }

    [Fact]
    public async Task Another_projects_payment_is_not_found()
    {
        await using var factory = new PaymentApiFactory { TestMode = true };
        await factory.SeedCredentialAsync(Token);
        await factory.SeedCredentialAsync("other-project-token", projectId: await factory.SeedProjectAsync());
        using var client = CreateClient(factory);
        var payment = await CreateSelectedPaymentAsync(client);
        using var otherClient = CreateClient(factory, "other-project-token");

        using var response = await otherClient.PostAsync(SimulatePath(payment.PaymentId), content: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("payment.not_found", await ProblemCodeAsync(response));
    }

    [Theory]
    [InlineData("0", "amount.not_positive")]
    [InlineData("abc", "amount.invalid")]
    [InlineData("0.000000001", "amount.invalid")]
    public async Task An_invalid_amount_is_a_validation_problem(string amount, string code)
    {
        await using var factory = new PaymentApiFactory { TestMode = true };
        await factory.SeedCredentialAsync(Token);
        using var client = CreateClient(factory);
        var payment = await CreateSelectedPaymentAsync(client);

        using var response = await SimulateAsync(client, payment.PaymentId, amount, idempotencyKey: null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(code, body.RootElement.GetProperty("errors").GetProperty("amount")[0].GetString());
    }

    [Fact]
    public async Task The_route_requires_the_integration_credential()
    {
        await using var factory = new PaymentApiFactory { TestMode = true };
        using var client = factory.CreateClient();

        using var response = await client.PostAsync(SimulatePath(Guid.NewGuid()), content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_live_installation_has_no_such_route()
    {
        await using var factory = new PaymentApiFactory();
        await factory.SeedCredentialAsync(Token);
        using var client = CreateClient(factory);
        var payment = await CreateSelectedPaymentAsync(client);

        using var response = await client.PostAsync(SimulatePath(payment.PaymentId), content: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("live", (await GetAsync(client, payment.PaymentId)).TestMode ? "test" : "live");
    }

    [Fact]
    public async Task The_payer_page_can_simulate_paying_with_nothing_but_its_link()
    {
        await using var factory = new PaymentApiFactory { TestMode = true };
        await factory.SeedCredentialAsync(Token);
        using var client = CreateClient(factory);
        var payment = await CreateSelectedPaymentAsync(client);
        var payerPageId = payment.PayerPageUrl[(payment.PayerPageUrl.LastIndexOf('/') + 1)..];
        using var browser = factory.CreateClient();

        using var response = await browser.PostAsJsonAsync(
            $"/api/payer/payments/{payerPageId}/simulated-transactions",
            new { amount = (string?)null });
        using var unknown = await browser.PostAsJsonAsync(
            "/api/payer/payments/not-a-payer-page/simulated-transactions",
            new { amount = (string?)null });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        await PollAsync(factory);
        await PollAsync(factory);
        Assert.Equal(PaymentStatus.Completed, (await GetAsync(client, payment.PaymentId)).Status);
    }

    [Fact]
    public async Task A_live_payer_page_has_no_simulation_route()
    {
        await using var factory = new PaymentApiFactory();
        await factory.SeedCredentialAsync(Token);
        using var client = CreateClient(factory);
        var payment = await CreateSelectedPaymentAsync(client);
        var payerPageId = payment.PayerPageUrl[(payment.PayerPageUrl.LastIndexOf('/') + 1)..];

        using var response = await factory.CreateClient().PostAsJsonAsync(
            $"/api/payer/payments/{payerPageId}/simulated-transactions",
            new { amount = (string?)null });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(PaymentStatus.WaitingForPayment, (await GetAsync(client, payment.PaymentId)).Status);
    }

    private static HttpClient CreateClient(PaymentApiFactory factory, string token = Token)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static string SimulatePath(Guid paymentId) => $"/api/v1/payments/{paymentId}/simulated-transactions";

    private static async Task<Payment> CreateSelectedPaymentAsync(HttpClient client)
    {
        var payaffe = new PayaffeClient(client, new Uri("http://localhost"), Token);
        var reference = $"order-{Guid.NewGuid():N}";
        var created = await payaffe.CreatePaymentAsync(new CreatePaymentRequest("EUR", 1999, reference), reference);
        return await payaffe.SelectCurrencyAsync(created.PaymentId, SupportedCurrency.Btc);
    }

    private static Task<Payment> GetAsync(HttpClient client, Guid paymentId) =>
        new PayaffeClient(client, new Uri("http://localhost"), Token).GetPaymentAsync(paymentId);

    private static async Task<HttpResponseMessage> SimulateAsync(
        HttpClient client,
        Guid paymentId,
        string amount,
        string? idempotencyKey)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, SimulatePath(paymentId))
        {
            Content = JsonContent.Create(new { amount }),
        };
        if (idempotencyKey is not null)
        {
            request.Headers.Add("Idempotency-Key", idempotencyKey);
        }

        return await client.SendAsync(request);
    }

    private static async Task PollAsync(PaymentApiFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<PaymentApplicationService>()
            .PollBlockchainObservationsAsync(10, CancellationToken.None);
    }

    private static async Task<string?> TransactionHashAsync(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("transactionHash").GetString();
    }

    private static async Task<string?> ProblemCodeAsync(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("code").GetString();
    }
}
