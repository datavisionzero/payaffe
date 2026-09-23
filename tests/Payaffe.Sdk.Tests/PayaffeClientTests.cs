using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;

namespace Payaffe.Sdk.Tests;

public sealed class PayaffeClientTests
{
    [Fact]
    public async Task Create_sends_authenticated_versioned_request_without_losing_integer_precision()
    {
        const long amount = 9_007_199_254_740_993;
        HttpRequestMessage? capturedRequest = null;
        string? capturedBody = null;
        Guid paymentId = Guid.NewGuid();
        using HttpClient httpClient = new(new DelegateHandler(async (request, cancellationToken) =>
        {
            capturedRequest = request;
            capturedBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            return JsonResponse(PaymentJson(paymentId, fiatAmountMinor: amount));
        }));
        PayaffeClient client = CreateClient(httpClient);

        Payment payment = await client.CreatePaymentAsync(
            new CreatePaymentRequest("EUR", amount, "order-123"),
            "stable-key");

        Assert.Equal(paymentId, payment.PaymentId);
        Assert.Equal(amount, payment.FiatAmountMinor);
        Assert.Equal(HttpMethod.Post, capturedRequest!.Method);
        Assert.Equal("https://payaffe.example.test/root/api/v1/payments", capturedRequest.RequestUri!.AbsoluteUri);
        Assert.Equal("Bearer", capturedRequest.Headers.Authorization!.Scheme);
        Assert.Equal("project-secret", capturedRequest.Headers.Authorization.Parameter);
        Assert.Equal("stable-key", capturedRequest.Headers.GetValues("Idempotency-Key").Single());
        using JsonDocument body = JsonDocument.Parse(capturedBody!);
        Assert.Equal(amount, body.RootElement.GetProperty("fiatAmountMinor").GetInt64());
    }

    [Fact]
    public async Task Unknown_string_values_are_preserved()
    {
        using HttpClient httpClient = new(new DelegateHandler((_, _) => Task.FromResult(
            JsonResponse(PaymentJson(
                Guid.NewGuid(),
                status: "awaiting_future_state",
                selectedCurrency: "DOGE",
                optionCurrency: "DOGE",
                optionStatus: "degraded")))));
        PayaffeClient client = CreateClient(httpClient);

        Payment payment = await client.GetPaymentAsync(Guid.NewGuid());

        Assert.Equal("awaiting_future_state", payment.Status.Value);
        Assert.Equal("DOGE", payment.SelectedCurrency!.Value.Value);
        Assert.Equal("DOGE", payment.PaymentOptions.Single().SupportedCurrency.Value);
        Assert.Equal("degraded", payment.PaymentOptions.Single().Status.Value);
    }

    [Fact]
    public async Task Validation_problem_exposes_stable_fields_and_never_the_token()
    {
        using HttpClient httpClient = new(new DelegateHandler((_, _) => Task.FromResult(
            ProblemResponse(
                HttpStatusCode.BadRequest,
                "validation.failed",
                "correlation-123",
                new Dictionary<string, string[]>
                {
                    ["fiatAmountMinor"] = ["fiat_amount.not_positive"],
                }))));
        PayaffeClient client = CreateClient(httpClient, maximumRetries: 0);

        PayaffeApiException exception = await Assert.ThrowsAsync<PayaffeApiException>(() =>
            client.CreatePaymentAsync(
                new CreatePaymentRequest("EUR", 0, "order-123"),
                "stable-key"));

        Assert.Equal(HttpStatusCode.BadRequest, exception.StatusCode);
        Assert.Equal("validation.failed", exception.Code.Value);
        Assert.Equal("correlation-123", exception.CorrelationId);
        Assert.Equal(
            "fiat_amount.not_positive",
            exception.ValidationErrors["fiatAmountMinor"].Single());
        Assert.DoesNotContain("project-secret", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Exhausted_rate_limit_exposes_retry_after()
    {
        HttpResponseMessage response = ProblemResponse(
            HttpStatusCode.TooManyRequests,
            "rate_limited",
            "correlation-429");
        response.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(7));
        using HttpClient httpClient = new(new DelegateHandler((_, _) => Task.FromResult(response)));
        PayaffeClient client = CreateClient(httpClient, maximumRetries: 0);

        PayaffeApiException exception = await Assert.ThrowsAsync<PayaffeApiException>(() =>
            client.GetPaymentAsync(Guid.NewGuid()));

        Assert.Equal(TimeSpan.FromSeconds(7), exception.RetryAfter);
        Assert.Equal("rate_limited", exception.Code.Value);
    }

    [Fact]
    public async Task Safe_selection_retry_reuses_the_same_request()
    {
        int attempts = 0;
        List<string> bodies = [];
        List<string> authorizationParameters = [];
        Guid paymentId = Guid.NewGuid();
        using HttpClient httpClient = new(new DelegateHandler(async (request, cancellationToken) =>
        {
            attempts++;
            bodies.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
            authorizationParameters.Add(request.Headers.Authorization!.Parameter!);
            if (attempts == 1)
            {
                HttpResponseMessage unavailable = ProblemResponse(
                    HttpStatusCode.ServiceUnavailable,
                    "unexpected_error",
                    "retry-me");
                unavailable.Headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.Zero);
                return unavailable;
            }

            return JsonResponse(PaymentJson(
                paymentId,
                status: "waiting_for_payment",
                selectedCurrency: "BTC"));
        }));
        PayaffeClient client = CreateClient(httpClient);

        Payment payment = await client.SelectCurrencyAsync(paymentId, SupportedCurrency.Btc);

        Assert.Equal(PaymentStatus.WaitingForPayment, payment.Status);
        Assert.Equal(2, attempts);
        Assert.Equal(bodies[0], bodies[1]);
        Assert.All(authorizationParameters, token => Assert.Equal("project-secret", token));
        using JsonDocument body = JsonDocument.Parse(bodies[0]);
        Assert.Equal("BTC", body.RootElement.GetProperty("supportedCurrency").GetString());
    }

    [Fact]
    public async Task Safe_creation_retry_preserves_body_and_idempotency_key()
    {
        int attempts = 0;
        List<string> bodies = [];
        List<string> idempotencyKeys = [];
        using HttpClient httpClient = new(new DelegateHandler(async (request, cancellationToken) =>
        {
            attempts++;
            bodies.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
            idempotencyKeys.Add(request.Headers.GetValues("Idempotency-Key").Single());
            return attempts == 1
                ? ProblemResponse(
                    HttpStatusCode.ServiceUnavailable,
                    "unexpected_error",
                    "retry-create")
                : JsonResponse(PaymentJson(Guid.NewGuid()));
        }));
        PayaffeClient client = CreateClient(httpClient);

        await client.CreatePaymentAsync(
            new CreatePaymentRequest("EUR", 1999, "order-123"),
            "stable-create-key");

        Assert.Equal(2, attempts);
        Assert.Equal(bodies[0], bodies[1]);
        Assert.Equal(["stable-create-key", "stable-create-key"], idempotencyKeys);
    }

    [Fact]
    public async Task Conflict_is_not_retried()
    {
        int attempts = 0;
        using HttpClient httpClient = new(new DelegateHandler((_, _) =>
        {
            attempts++;
            return Task.FromResult(ProblemResponse(
                HttpStatusCode.Conflict,
                "payment.currency_already_selected",
                "conflict"));
        }));
        PayaffeClient client = CreateClient(httpClient);

        await Assert.ThrowsAsync<PayaffeApiException>(() =>
            client.SelectCurrencyAsync(Guid.NewGuid(), SupportedCurrency.Btc));

        Assert.Equal(1, attempts);
    }

    [Fact]
    public async Task Caller_cancellation_remains_an_operation_cancellation()
    {
        using HttpClient httpClient = new(new DelegateHandler(async (_, cancellationToken) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("Unreachable.");
        }));
        PayaffeClient client = CreateClient(httpClient);
        using CancellationTokenSource cancellation = new();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            client.GetPaymentAsync(Guid.NewGuid(), cancellation.Token));
    }

    [Fact]
    public async Task Transport_timeout_remains_distinct_from_an_api_error()
    {
        using HttpClient httpClient = new(new DelegateHandler((_, _) =>
            throw new TaskCanceledException("The request timed out.")));
        PayaffeClient client = CreateClient(httpClient, maximumRetries: 0);

        Exception exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            client.GetPaymentAsync(Guid.NewGuid()));

        Assert.IsNotType<PayaffeApiException>(exception);
    }

    [Fact]
    public async Task Ambiguous_creation_timeout_retries_with_the_original_key()
    {
        int attempts = 0;
        List<string> idempotencyKeys = [];
        using HttpClient httpClient = new(new DelegateHandler((request, _) =>
        {
            attempts++;
            idempotencyKeys.Add(request.Headers.GetValues("Idempotency-Key").Single());
            if (attempts == 1)
            {
                throw new TaskCanceledException("The first request timed out ambiguously.");
            }

            return Task.FromResult(JsonResponse(PaymentJson(Guid.NewGuid())));
        }));
        PayaffeClient client = CreateClient(httpClient);

        await client.CreatePaymentAsync(
            new CreatePaymentRequest("EUR", 1999, "order-timeout"),
            "stable-timeout-key");

        Assert.Equal(2, attempts);
        Assert.Equal(["stable-timeout-key", "stable-timeout-key"], idempotencyKeys);
    }

    [Fact]
    public async Task Polling_yields_states_and_stops_on_a_terminal_status()
    {
        Queue<string> statuses = new(["waiting_for_payment", "observed", "completed"]);
        using HttpClient httpClient = new(new DelegateHandler((_, _) => Task.FromResult(
            JsonResponse(PaymentJson(Guid.NewGuid(), status: statuses.Dequeue())))));
        PayaffeClient client = CreateClient(httpClient);
        List<PaymentStatus> observed = [];

        await foreach (Payment payment in client.PollPaymentAsync(
            Guid.NewGuid(),
            new PaymentPollingOptions
            {
                InitialInterval = TimeSpan.FromMilliseconds(1),
                MaximumInterval = TimeSpan.FromMilliseconds(2),
                JitterRatio = 0,
            }))
        {
            observed.Add(payment.Status);
        }

        Assert.Equal(
            [PaymentStatus.WaitingForPayment, PaymentStatus.Observed, PaymentStatus.Completed],
            observed);
        Assert.Empty(statuses);
    }

    [Fact]
    public async Task Named_clients_keep_project_credentials_and_addresses_separate()
    {
        List<(string Host, string Token)> requests = [];
        ServiceCollection services = new();
        services.AddPayaffeClient("project-a", options =>
            {
                options.BaseAddress = new Uri("https://a.example.test/");
                options.ApiToken = "token-a";
            })
            .ConfigurePrimaryHttpMessageHandler(() => CaptureHandler(requests));
        services.AddPayaffeClient("project-b", options =>
            {
                options.BaseAddress = new Uri("https://b.example.test/");
                options.ApiToken = "token-b";
            })
            .ConfigurePrimaryHttpMessageHandler(() => CaptureHandler(requests));
        await using ServiceProvider provider = services.BuildServiceProvider();
        IPayaffeClientFactory factory = provider.GetRequiredService<IPayaffeClientFactory>();

        await factory.CreateClient("project-a").GetPaymentAsync(Guid.NewGuid());
        await factory.CreateClient("project-b").GetPaymentAsync(Guid.NewGuid());

        Assert.Equal(
            [("a.example.test", "token-a"), ("b.example.test", "token-b")],
            requests);
    }

    [Theory]
    [InlineData("relative/path")]
    [InlineData("ftp://payaffe.example.test")]
    [InlineData("https://user:password@payaffe.example.test")]
    [InlineData("https://payaffe.example.test?token=secret")]
    public void Base_address_must_be_a_safe_absolute_http_url(string value)
    {
        using HttpClient httpClient = new(new DelegateHandler((_, _) =>
            Task.FromResult(JsonResponse(PaymentJson(Guid.NewGuid())))));

        Assert.Throws<ArgumentException>(() =>
            new PayaffeClient(httpClient, new Uri(value, UriKind.RelativeOrAbsolute), "token"));
    }

    [Fact]
    public async Task Simulating_a_payment_posts_the_amount_and_idempotency_key()
    {
        Guid paymentId = Guid.NewGuid();
        HttpRequestMessage? capturedRequest = null;
        string? capturedBody = null;
        using HttpClient httpClient = new(new DelegateHandler(async (request, cancellationToken) =>
        {
            capturedRequest = request;
            capturedBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.Created)
            {
                Content = new StringContent(
                    $$"""
                    {
                      "paymentId": "{{paymentId}}",
                      "supportedCurrency": "BTC",
                      "paymentAddress": "tb1qsimulated",
                      "transactionHash": "{{new string('a', 64)}}",
                      "amount": "0.0002",
                      "recordedAt": "2026-09-23T12:00:00+00:00"
                    }
                    """,
                    Encoding.UTF8,
                    "application/json"),
            };
        }));
        PayaffeClient client = CreateClient(httpClient);

        SimulatedTransaction transaction = await client.SimulatePaymentAsync(paymentId, "0.0002", "attempt-1");

        Assert.Equal(paymentId, transaction.PaymentId);
        Assert.Equal(SupportedCurrency.Btc, transaction.SupportedCurrency);
        Assert.Equal("0.0002", transaction.Amount);
        Assert.Equal(HttpMethod.Post, capturedRequest!.Method);
        Assert.Equal(
            $"https://payaffe.example.test/root/api/v1/payments/{paymentId}/simulated-transactions",
            capturedRequest.RequestUri!.AbsoluteUri);
        Assert.Equal("attempt-1", capturedRequest.Headers.GetValues("Idempotency-Key").Single());
        using JsonDocument body = JsonDocument.Parse(capturedBody!);
        Assert.Equal("0.0002", body.RootElement.GetProperty("amount").GetString());
    }

    [Fact]
    public async Task Simulating_against_a_live_installation_says_so()
    {
        using HttpClient httpClient = new(new DelegateHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound))));
        PayaffeClient client = CreateClient(httpClient);

        PayaffeApiException exception = await Assert.ThrowsAsync<PayaffeApiException>(
            () => client.SimulatePaymentAsync(Guid.NewGuid()));

        Assert.Equal(PayaffeErrorCode.TestModeUnavailable, exception.Code);
        Assert.Contains("not in Test Mode", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_missing_payment_in_a_test_installation_is_not_mistaken_for_a_live_one()
    {
        using HttpClient httpClient = new(new DelegateHandler((_, _) => Task.FromResult(
            ProblemResponse(HttpStatusCode.NotFound, "payment.not_found", "correlation-1"))));
        PayaffeClient client = CreateClient(httpClient);

        PayaffeApiException exception = await Assert.ThrowsAsync<PayaffeApiException>(
            () => client.SimulatePaymentAsync(Guid.NewGuid()));

        Assert.Equal("payment.not_found", exception.Code.Value);
    }

    [Theory]
    [InlineData(null, 1)]
    [InlineData("attempt-1", 3)]
    public async Task A_simulation_is_retried_after_a_lost_answer_only_with_an_idempotency_key(
        string? idempotencyKey,
        int expectedAttempts)
    {
        int attempts = 0;
        using HttpClient httpClient = new(new DelegateHandler((_, _) =>
        {
            attempts++;
            throw new HttpRequestException("connection reset");
        }));
        PayaffeClient client = CreateClient(httpClient, maximumRetries: 2);

        await Assert.ThrowsAsync<HttpRequestException>(
            () => client.SimulatePaymentAsync(Guid.NewGuid(), idempotencyKey: idempotencyKey));

        Assert.Equal(expectedAttempts, attempts);
    }

    private static PayaffeClient CreateClient(HttpClient httpClient, int maximumRetries = 2)
    {
        return new PayaffeClient(
            httpClient,
            new PayaffeClientOptions
            {
                BaseAddress = new Uri("https://payaffe.example.test/root/"),
                ApiToken = "project-secret",
                MaximumRetries = maximumRetries,
                InitialRetryDelay = TimeSpan.FromMilliseconds(1),
                MaximumRetryDelay = TimeSpan.FromMilliseconds(2),
                RetryJitterRatio = 0,
            });
    }

    private static HttpMessageHandler CaptureHandler(List<(string Host, string Token)> requests)
    {
        return new DelegateHandler((request, _) =>
        {
            requests.Add((request.RequestUri!.Host, request.Headers.Authorization!.Parameter!));
            return Task.FromResult(JsonResponse(PaymentJson(Guid.NewGuid())));
        });
    }

    private static HttpResponseMessage JsonResponse(string json) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };

    private static HttpResponseMessage ProblemResponse(
        HttpStatusCode statusCode,
        string code,
        string correlationId,
        Dictionary<string, string[]>? errors = null)
    {
        string json = JsonSerializer.Serialize(new
        {
            type = "https://payaffe.example.test/problems/test",
            title = "Request failed.",
            status = (int)statusCode,
            code,
            correlationId,
            errors,
        });
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/problem+json"),
        };
    }

    private static string PaymentJson(
        Guid paymentId,
        string status = "pending_currency_selection",
        long fiatAmountMinor = 1999,
        string? selectedCurrency = null,
        string optionCurrency = "BTC",
        string optionStatus = "available")
    {
        DateTimeOffset now = DateTimeOffset.Parse("2026-09-18T12:00:00Z");
        return JsonSerializer.Serialize(new
        {
            paymentId,
            status,
            payerPageUrl = $"https://payaffe.example.test/pay/{paymentId:N}",
            expiresAt = now.AddMinutes(30),
            fiatCurrency = "EUR",
            fiatAmountMinor,
            externalReference = "order-123",
            selectedCurrency,
            expectedCryptoAmount = selectedCurrency is null ? null : "0.00039980",
            paymentAddress = selectedCurrency is null ? null : "bc1qexample",
            observedTotal = (string?)null,
            completedAt = status == "completed" ? now.AddMinutes(2) : (DateTimeOffset?)null,
            settledAt = (DateTimeOffset?)null,
            returnUrl = "https://shop.example.test/orders/order-123",
            paymentOptions = new[]
            {
                new
                {
                    supportedCurrency = optionCurrency,
                    status = optionStatus,
                    unavailableReasonCode = (string?)null,
                    checkedAt = now,
                },
            },
            createdAt = now,
            updatedAt = now,
            lateAcceptanceEndsAt = now.AddHours(1),
            confirmedEligibleTotal = (string?)null,
            observedAmountState = "none",
            rateLock = (object?)null,
            paymentInstruction = (object?)null,
        });
    }

    private sealed class DelegateHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) :
        HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => send(request, cancellationToken);
    }
}
