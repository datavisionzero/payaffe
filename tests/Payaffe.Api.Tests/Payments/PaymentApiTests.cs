using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Payaffe.Application.Payments;
using Payaffe.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
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
        await AcceptCredentialAsync(client);
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

    /// <summary>
    /// The contract limits each route, not each URL: a caller that walks
    /// through Payment IDs does not get a fresh budget for every one.
    /// </summary>
    [Fact]
    public async Task Rate_limit_is_shared_by_every_Payment_ID_on_a_route()
    {
        await using var factory = new PaymentApiFactory
        {
            IntegrationApiRateLimitPermitLimit = 1,
            IntegrationApiRateLimitWindow = TimeSpan.FromMinutes(1),
        };
        await factory.SeedCredentialAsync(ValidToken);
        using var client = CreateAuthenticatedClient(factory);
        await AcceptCredentialAsync(client);

        var first = await client.GetAsync($"/api/v1/payments/{Guid.NewGuid()}");
        var second = await client.GetAsync($"/api/v1/payments/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, first.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);
    }

    /// <summary>
    /// An invented token is not a partition of its own: otherwise every junk
    /// Authorization value would open a fresh budget and a new partition.
    /// </summary>
    [Fact]
    public async Task Tokens_never_accepted_share_the_budget_of_their_route_and_address()
    {
        await using var factory = new PaymentApiFactory
        {
            IntegrationApiRateLimitPermitLimit = 1,
            IntegrationApiRateLimitWindow = TimeSpan.FromMinutes(1),
        };
        using var client = factory.CreateClient();
        var paymentId = Guid.NewGuid();

        var first = await SendWithTokenAsync(client, paymentId, "junk-token-1");
        var second = await SendWithTokenAsync(client, paymentId, "junk-token-2");

        Assert.Equal(HttpStatusCode.Unauthorized, first.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);
    }

    [Fact]
    public async Task Rate_limit_rejections_are_audited_once_per_partition_and_window()
    {
        await using var factory = new PaymentApiFactory
        {
            IntegrationApiRateLimitPermitLimit = 1,
            IntegrationApiRateLimitWindow = TimeSpan.FromMinutes(1),
        };
        using var client = factory.CreateClient();
        var paymentId = Guid.NewGuid();

        var responses = new List<HttpResponseMessage>();
        for (var attempt = 0; attempt < 5; attempt++)
        {
            responses.Add(await SendWithTokenAsync(client, paymentId, $"junk-token-{attempt}"));
        }

        Assert.Equal(4, responses.Count(response => response.StatusCode == HttpStatusCode.TooManyRequests));
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PayaffeDbContext>();
        Assert.Single(dbContext.AuditLogEntries, entry => entry.EventType == "integration_api.rate_limit");
    }

    private static async Task<HttpResponseMessage> SendWithTokenAsync(HttpClient client, Guid paymentId, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/payments/{paymentId}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.SendAsync(request);
    }

    /// <summary>
    /// A token gets a rate-limit partition of its own once this host has
    /// accepted it; until then it counts with the callers of its route and
    /// address that present none. One request on another route settles that
    /// without spending the budget under test.
    /// </summary>
    private static async Task AcceptCredentialAsync(HttpClient client)
    {
        using var content = JsonContent.Create(new { });
        var response = await client.PostAsync("/api/v1/payments", content);
        Assert.NotEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.NotEqual(HttpStatusCode.TooManyRequests, response.StatusCode);
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
    public async Task Credential_can_read_a_payment_created_by_another_credential_in_its_project()
    {
        await using var factory = new PaymentApiFactory();
        await factory.SeedCredentialAsync("project-token-a");
        await factory.SeedCredentialAsync("project-token-b");
        using var creator = CreateAuthenticatedClient(factory, "project-token-a");
        creator.DefaultRequestHeaders.Add("Idempotency-Key", "shared-project-read");
        var createResponse = await creator.PostAsJsonAsync("/api/v1/payments", ValidCreatePaymentRequest());
        var createdPayment = await createResponse.Content.ReadFromJsonAsync<PaymentApiResponse>();
        using var reader = CreateAuthenticatedClient(factory, "project-token-b");

        var response = await reader.GetAsync($"/api/v1/payments/{createdPayment!.PaymentId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Cross_project_payment_id_and_spoofed_project_header_return_safe_not_found()
    {
        await using var factory = new PaymentApiFactory();
        await factory.SeedCredentialAsync("default-project-token");
        var otherProjectId = await factory.SeedProjectAsync();
        await factory.SeedCredentialAsync("other-project-token", projectId: otherProjectId);
        using var creator = CreateAuthenticatedClient(factory, "default-project-token");
        creator.DefaultRequestHeaders.Add("Idempotency-Key", "cross-project-read");
        var createResponse = await creator.PostAsJsonAsync("/api/v1/payments", ValidCreatePaymentRequest());
        var createdPayment = await createResponse.Content.ReadFromJsonAsync<PaymentApiResponse>();
        using var reader = CreateAuthenticatedClient(factory, "other-project-token");
        reader.DefaultRequestHeaders.Add("X-Payaffe-Project-Id", ProjectDefaults.DefaultProjectId.ToString("D"));

        var response = await reader.GetAsync($"/api/v1/payments/{createdPayment!.PaymentId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await AssertProblemCodeAsync(response, "payment.not_found");
    }

    [Fact]
    public async Task Projects_can_reuse_the_same_idempotency_key_and_external_reference()
    {
        await using var factory = new PaymentApiFactory();
        await factory.SeedCredentialAsync("first-project-token");
        var otherProjectId = await factory.SeedProjectAsync();
        await factory.SeedCredentialAsync("second-project-token", projectId: otherProjectId);
        using var first = CreateAuthenticatedClient(factory, "first-project-token");
        using var second = CreateAuthenticatedClient(factory, "second-project-token");
        first.DefaultRequestHeaders.Add("Idempotency-Key", "same-key");
        second.DefaultRequestHeaders.Add("Idempotency-Key", "same-key");

        var firstResponse = await first.PostAsJsonAsync("/api/v1/payments", ValidCreatePaymentRequest());
        var secondResponse = await second.PostAsJsonAsync("/api/v1/payments", ValidCreatePaymentRequest());
        var firstPayment = await firstResponse.Content.ReadFromJsonAsync<PaymentApiResponse>();
        var secondPayment = await secondResponse.Content.ReadFromJsonAsync<PaymentApiResponse>();

        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Created, secondResponse.StatusCode);
        Assert.NotEqual(firstPayment!.PaymentId, secondPayment!.PaymentId);
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PayaffeDbContext>();
        Assert.Equal(2, dbContext.Payments.Select(payment => payment.ProjectId).Distinct().Count());
    }

    [Fact]
    public async Task Disabled_project_rejects_new_payment_but_allows_existing_payment_polling()
    {
        await using var factory = new PaymentApiFactory();
        var projectId = await factory.SeedProjectAsync();
        await factory.SeedCredentialAsync("disabled-project-token", projectId: projectId);
        using var client = CreateAuthenticatedClient(factory, "disabled-project-token");
        client.DefaultRequestHeaders.Add("Idempotency-Key", "before-project-disabled");
        var createResponse = await client.PostAsJsonAsync("/api/v1/payments", ValidCreatePaymentRequest());
        var createdPayment = await createResponse.Content.ReadFromJsonAsync<PaymentApiResponse>();
        using (var scope = factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<PayaffeDbContext>();
            var project = await dbContext.Projects.SingleAsync(candidate => candidate.Id == projectId);
            project.Status = "disabled";
            await dbContext.SaveChangesAsync();
        }

        var existingResponse = await client.GetAsync($"/api/v1/payments/{createdPayment!.PaymentId}");
        client.DefaultRequestHeaders.Remove("Idempotency-Key");
        client.DefaultRequestHeaders.Add("Idempotency-Key", "disabled-project-create");

        var response = await client.PostAsJsonAsync("/api/v1/payments", ValidCreatePaymentRequest());

        Assert.Equal(HttpStatusCode.OK, existingResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        await AssertProblemCodeAsync(response, "project.not_active");
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
        Assert.Equal("bc1qpayaffetestaddress0000000000000000000000000", payment.PaymentAddress);
    }

    [Theory]
    [InlineData(
        "BTC",
        "39980",
        "bc1qpayaffetestaddress0000000000000000000000000",
        "bitcoin:bc1qpayaffetestaddress0000000000000000000000000?amount=0.00039980",
        null)]
    [InlineData(
        "LTC",
        "39980",
        "ltc1qpayaffetestaddress000000000000000000000000",
        "litecoin:ltc1qpayaffetestaddress000000000000000000000000?amount=0.00039980",
        null)]
    [InlineData(
        "ETH",
        "399800000000000",
        "0x1111111111111111111111111111111111111111",
        "ethereum:0x1111111111111111111111111111111111111111@1?value=399800000000000",
        1L)]
    public async Task Integration_currency_selection_returns_exact_wallet_instruction(
        string supportedCurrency,
        string amountAtomic,
        string paymentAddress,
        string paymentUri,
        long? chainId)
    {
        await using var factory = new PaymentApiFactory();
        await factory.SeedCredentialAsync(ValidToken);
        using var client = CreateAuthenticatedClient(factory);
        client.DefaultRequestHeaders.Add("Idempotency-Key", $"instruction-{supportedCurrency}");
        var createResponse = await client.PostAsJsonAsync("/api/v1/payments", ValidCreatePaymentRequest());
        var createdPayment = await createResponse.Content.ReadFromJsonAsync<PaymentApiResponse>();

        var response = await client.PutAsJsonAsync(
            $"/api/v1/payments/{createdPayment!.PaymentId}/currency-selection",
            new { supportedCurrency });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var selected = await response.Content.ReadFromJsonAsync<PaymentApiResponse>();
        Assert.NotNull(selected);
        Assert.Equal("waiting_for_payment", selected.Status);
        Assert.Equal("none", selected.ObservedAmountState);
        Assert.Equal(selected.ExpiresAt, selected.LateAcceptanceEndsAt.AddHours(-24));
        Assert.NotEqual(default, selected.CreatedAt);
        Assert.NotEqual(default, selected.UpdatedAt);

        Assert.NotNull(selected.RateLock);
        Assert.Equal("EUR", selected.RateLock.FiatCurrency);
        Assert.Equal(1999, selected.RateLock.FiatAmountMinor);
        Assert.Equal(supportedCurrency, selected.RateLock.SupportedCurrency);
        Assert.Equal("0.00039980", selected.RateLock.ExpectedCryptoAmount);
        Assert.Equal(amountAtomic, selected.RateLock.ExpectedCryptoAmountAtomic);
        Assert.Equal("50000.00", selected.RateLock.FiatPerCryptoUnit);
        Assert.Equal("test-rate-source", selected.RateLock.Source);
        Assert.Equal(selected.ExpiresAt, selected.RateLock.ValidUntil);

        Assert.NotNull(selected.PaymentInstruction);
        Assert.Equal(supportedCurrency, selected.PaymentInstruction.SupportedCurrency);
        Assert.Equal("mainnet", selected.PaymentInstruction.Network);
        Assert.Equal(chainId, selected.PaymentInstruction.ChainId);
        Assert.Equal("0.00039980", selected.PaymentInstruction.Amount);
        Assert.Equal(amountAtomic, selected.PaymentInstruction.AmountAtomic);
        Assert.Equal(paymentAddress, selected.PaymentInstruction.PaymentAddress);
        Assert.Equal(paymentUri, selected.PaymentInstruction.Uri);
        Assert.Equal(selected.ExpiresAt, selected.PaymentInstruction.ExpiresAt);

        var pollResponse = await client.GetAsync($"/api/v1/payments/{createdPayment.PaymentId}");
        var polled = await pollResponse.Content.ReadFromJsonAsync<PaymentApiResponse>();
        Assert.Equal(selected.PaymentInstruction, polled!.PaymentInstruction);
        Assert.Equal(selected.RateLock, polled.RateLock);
    }

    [Fact]
    public async Task Get_payment_reports_received_and_expected_amount_state()
    {
        await using var factory = new PaymentApiFactory();
        await factory.SeedCredentialAsync(ValidToken);
        using var client = CreateAuthenticatedClient(factory);
        client.DefaultRequestHeaders.Add("Idempotency-Key", "observed-amount-state");
        var createResponse = await client.PostAsJsonAsync("/api/v1/payments", ValidCreatePaymentRequest());
        var created = await createResponse.Content.ReadFromJsonAsync<PaymentApiResponse>();
        var selectionResponse = await client.PutAsJsonAsync(
            $"/api/v1/payments/{created!.PaymentId}/currency-selection",
            new { supportedCurrency = "BTC" });
        var selected = await selectionResponse.Content.ReadFromJsonAsync<PaymentApiResponse>();

        using (var scope = factory.Services.CreateScope())
        {
            var service = scope.ServiceProvider.GetRequiredService<PaymentApplicationService>();
            await service.RecordBlockchainObservationAsync(
                new RecordBlockchainObservationCommand(
                    created.PaymentId,
                    "BTC",
                    selected!.PaymentAddress!,
                    "tx-underpaid",
                    "0.00010000",
                    DateTimeOffset.UtcNow,
                    0,
                    "test-provider",
                    "observation-underpaid",
                    ProjectDefaults.DefaultProjectId),
                CancellationToken.None);
        }

        var response = await client.GetAsync($"/api/v1/payments/{created.PaymentId}");
        var payment = await response.Content.ReadFromJsonAsync<PaymentApiResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("0.0001", payment!.ObservedTotal);
        Assert.Null(payment.ConfirmedEligibleTotal);
        Assert.Equal("0.00039980", payment.ExpectedCryptoAmount);
        Assert.Equal("underpaid", payment.ObservedAmountState);
    }

    [Fact]
    public async Task Integration_currency_selection_requires_authentication()
    {
        await using var factory = new PaymentApiFactory();
        using var client = factory.CreateClient();

        var response = await client.PutAsJsonAsync(
            $"/api/v1/payments/{Guid.NewGuid()}/currency-selection",
            new { supportedCurrency = "BTC" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        await AssertProblemCodeAsync(response, "authentication.required");
    }

    [Fact]
    public async Task Integration_currency_selection_is_idempotent_and_rejects_a_different_currency()
    {
        await using var factory = new PaymentApiFactory();
        await factory.SeedCredentialAsync(ValidToken);
        using var client = CreateAuthenticatedClient(factory);
        client.DefaultRequestHeaders.Add("Idempotency-Key", "integration-select-currency");
        var createResponse = await client.PostAsJsonAsync("/api/v1/payments", ValidCreatePaymentRequest());
        var createdPayment = await createResponse.Content.ReadFromJsonAsync<PaymentApiResponse>();

        var selectedResponse = await client.PutAsJsonAsync(
            $"/api/v1/payments/{createdPayment!.PaymentId}/currency-selection",
            new { supportedCurrency = "btc" });
        var replayResponse = await client.PutAsJsonAsync(
            $"/api/v1/payments/{createdPayment.PaymentId}/currency-selection",
            new { supportedCurrency = "BTC" });
        var conflictResponse = await client.PutAsJsonAsync(
            $"/api/v1/payments/{createdPayment.PaymentId}/currency-selection",
            new { supportedCurrency = "LTC" });

        Assert.Equal(HttpStatusCode.OK, selectedResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, replayResponse.StatusCode);
        var selected = await selectedResponse.Content.ReadFromJsonAsync<PaymentApiResponse>();
        var replayed = await replayResponse.Content.ReadFromJsonAsync<PaymentApiResponse>();
        Assert.Equal("waiting_for_payment", selected!.Status);
        Assert.Equal("BTC", selected.SelectedCurrency);
        Assert.Equal(selected.ExpectedCryptoAmount, replayed!.ExpectedCryptoAmount);
        Assert.Equal(selected.PaymentAddress, replayed.PaymentAddress);
        Assert.Equal(HttpStatusCode.Conflict, conflictResponse.StatusCode);
        await AssertProblemCodeAsync(conflictResponse, "payment.currency_already_selected");

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PayaffeDbContext>();
        Assert.Single(dbContext.RateLocks.Where(rateLock => rateLock.PaymentId == createdPayment.PaymentId));
        Assert.Single(dbContext.PaymentAddressAssignments.Where(
            assignment => assignment.PaymentId == createdPayment.PaymentId));
    }

    [Fact]
    public async Task Integration_currency_selection_hides_another_projects_payment()
    {
        await using var factory = new PaymentApiFactory();
        await factory.SeedCredentialAsync("owner-token");
        var otherProjectId = await factory.SeedProjectAsync();
        await factory.SeedCredentialAsync("other-token", projectId: otherProjectId);
        using var owner = CreateAuthenticatedClient(factory, "owner-token");
        owner.DefaultRequestHeaders.Add("Idempotency-Key", "cross-project-selection");
        var createResponse = await owner.PostAsJsonAsync("/api/v1/payments", ValidCreatePaymentRequest());
        var createdPayment = await createResponse.Content.ReadFromJsonAsync<PaymentApiResponse>();
        using var other = CreateAuthenticatedClient(factory, "other-token");

        var response = await other.PutAsJsonAsync(
            $"/api/v1/payments/{createdPayment!.PaymentId}/currency-selection",
            new { supportedCurrency = "BTC" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        await AssertProblemCodeAsync(response, "payment.not_found");
    }

    [Theory]
    [InlineData("rate", "exchange_rate.unavailable")]
    [InlineData("address", "payment_address.unavailable")]
    [InlineData("observation", "blockchain_observation.unavailable")]
    public async Task Integration_currency_selection_rechecks_live_availability(
        string unavailableDependency,
        string expectedCode)
    {
        await using var factory = new PaymentApiFactory();
        await factory.SeedCredentialAsync(ValidToken);
        using var client = CreateAuthenticatedClient(factory);
        client.DefaultRequestHeaders.Add("Idempotency-Key", $"unavailable-{unavailableDependency}");
        var createResponse = await client.PostAsJsonAsync("/api/v1/payments", ValidCreatePaymentRequest());
        var createdPayment = await createResponse.Content.ReadFromJsonAsync<PaymentApiResponse>();
        var unavailableCurrencies = unavailableDependency switch
        {
            "rate" => factory.UnavailableRateCurrencies,
            "address" => factory.UnavailableAddressCurrencies,
            "observation" => factory.UnavailableObservationCurrencies,
            _ => throw new ArgumentOutOfRangeException(nameof(unavailableDependency)),
        };
        unavailableCurrencies.Add("BTC");

        var response = await client.PutAsJsonAsync(
            $"/api/v1/payments/{createdPayment!.PaymentId}/currency-selection",
            new { supportedCurrency = "BTC" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        await AssertProblemCodeAsync(response, expectedCode);
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PayaffeDbContext>();
        var stored = await dbContext.Payments.SingleAsync(payment => payment.Id == createdPayment.PaymentId);
        Assert.Equal("pending_currency_selection", stored.Status);
        Assert.Empty(dbContext.RateLocks.Where(rateLock => rateLock.PaymentId == createdPayment.PaymentId));
    }

    [Fact]
    public async Task Integration_currency_selection_rejects_disabled_currency_and_expired_payment()
    {
        await using var factory = new PaymentApiFactory();
        await factory.SeedCredentialAsync(ValidToken);
        using var client = CreateAuthenticatedClient(factory);
        client.DefaultRequestHeaders.Add("Idempotency-Key", "disabled-currency");
        var disabledCreateResponse = await client.PostAsJsonAsync("/api/v1/payments", ValidCreatePaymentRequest());
        var disabledPayment = await disabledCreateResponse.Content.ReadFromJsonAsync<PaymentApiResponse>();
        await factory.SetCurrencyEnabledAsync(ProjectDefaults.DefaultProjectId, "BTC", enabled: false);

        var disabledResponse = await client.PutAsJsonAsync(
            $"/api/v1/payments/{disabledPayment!.PaymentId}/currency-selection",
            new { supportedCurrency = "BTC" });

        Assert.Equal(HttpStatusCode.Conflict, disabledResponse.StatusCode);
        await AssertProblemCodeAsync(disabledResponse, "project_configuration.disabled");

        await factory.SetCurrencyEnabledAsync(ProjectDefaults.DefaultProjectId, "BTC", enabled: true);
        client.DefaultRequestHeaders.Remove("Idempotency-Key");
        client.DefaultRequestHeaders.Add("Idempotency-Key", "expired-selection");
        var expiredCreateResponse = await client.PostAsJsonAsync("/api/v1/payments", ValidCreatePaymentRequest());
        var expiredPayment = await expiredCreateResponse.Content.ReadFromJsonAsync<PaymentApiResponse>();
        await factory.ExpirePaymentAsync(expiredPayment!.PaymentId);

        var expiredResponse = await client.PutAsJsonAsync(
            $"/api/v1/payments/{expiredPayment.PaymentId}/currency-selection",
            new { supportedCurrency = "BTC" });

        Assert.Equal(HttpStatusCode.Conflict, expiredResponse.StatusCode);
        await AssertProblemCodeAsync(expiredResponse, "payment.expired");
    }

    [Theory]
    [InlineData("disabled", HttpStatusCode.OK, null)]
    [InlineData("archived", HttpStatusCode.Conflict, "project.archived")]
    public async Task Integration_currency_selection_respects_project_lifecycle(
        string projectStatus,
        HttpStatusCode expectedStatus,
        string? expectedCode)
    {
        await using var factory = new PaymentApiFactory();
        await factory.SeedCredentialAsync(ValidToken);
        using var client = CreateAuthenticatedClient(factory);
        client.DefaultRequestHeaders.Add("Idempotency-Key", $"project-{projectStatus}");
        var createResponse = await client.PostAsJsonAsync("/api/v1/payments", ValidCreatePaymentRequest());
        var createdPayment = await createResponse.Content.ReadFromJsonAsync<PaymentApiResponse>();
        await factory.SetProjectStatusAsync(ProjectDefaults.DefaultProjectId, projectStatus);

        var response = await client.PutAsJsonAsync(
            $"/api/v1/payments/{createdPayment!.PaymentId}/currency-selection",
            new { supportedCurrency = "BTC" });

        Assert.Equal(expectedStatus, response.StatusCode);
        if (expectedCode is not null)
        {
            await AssertProblemCodeAsync(response, expectedCode);
        }
    }

    private static HttpClient CreateAuthenticatedClient(
        PaymentApiFactory factory,
        string token = ValidToken)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
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
        string? ReturnUrl,
        IReadOnlyList<PaymentOptionApiResponse>? PaymentOptions = null,
        DateTimeOffset CreatedAt = default,
        DateTimeOffset UpdatedAt = default,
        DateTimeOffset LateAcceptanceEndsAt = default,
        string? ConfirmedEligibleTotal = null,
        string? ObservedAmountState = null,
        RateLockApiResponse? RateLock = null,
        PaymentInstructionApiResponse? PaymentInstruction = null);

    private sealed record PaymentOptionApiResponse(
        string SupportedCurrency,
        string Status,
        string? UnavailableReasonCode,
        DateTimeOffset CheckedAt);

    private sealed record RateLockApiResponse(
        string FiatCurrency,
        long FiatAmountMinor,
        string SupportedCurrency,
        string ExpectedCryptoAmount,
        string ExpectedCryptoAmountAtomic,
        string FiatPerCryptoUnit,
        string Source,
        DateTimeOffset RateObservedAt,
        DateTimeOffset LockedAt,
        DateTimeOffset ValidUntil);

    private sealed record PaymentInstructionApiResponse(
        string SupportedCurrency,
        string Network,
        long? ChainId,
        string Amount,
        string AmountAtomic,
        string PaymentAddress,
        string Uri,
        DateTimeOffset ExpiresAt);
}
