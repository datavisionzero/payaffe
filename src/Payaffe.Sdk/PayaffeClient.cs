using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Payaffe.Sdk;

public sealed class PayaffeClient
{
    private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;
    private readonly ValidatedPayaffeClientOptions _options;

    public PayaffeClient(HttpClient httpClient, Uri baseAddress, string apiToken)
        : this(
            httpClient,
            new PayaffeClientOptions
            {
                BaseAddress = baseAddress,
                ApiToken = apiToken,
            }.Validate())
    {
    }

    public PayaffeClient(HttpClient httpClient, PayaffeClientOptions options)
        : this(httpClient, (options ?? throw new ArgumentNullException(nameof(options))).Validate())
    {
    }

    internal PayaffeClient(HttpClient httpClient, ValidatedPayaffeClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        _httpClient = httpClient;
        _options = options;
    }

    public Task<Payment> CreatePaymentAsync(
        CreatePaymentRequest request,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);

        return SendPaymentAsync(
            () =>
            {
                HttpRequestMessage message = CreateRequest(HttpMethod.Post, "payments");
                message.Headers.Add("Idempotency-Key", idempotencyKey);
                message.Content = JsonContent.Create(request, options: _jsonOptions);
                return message;
            },
            cancellationToken);
    }

    public Task<Payment> GetPaymentAsync(
        Guid paymentId,
        CancellationToken cancellationToken = default)
    {
        if (paymentId == Guid.Empty)
        {
            throw new ArgumentException("A Payment identifier is required.", nameof(paymentId));
        }

        return SendPaymentAsync(
            () => CreateRequest(HttpMethod.Get, $"payments/{paymentId:D}"),
            cancellationToken);
    }

    public Task<Payment> SelectCurrencyAsync(
        Guid paymentId,
        SupportedCurrency currency,
        CancellationToken cancellationToken = default)
    {
        if (paymentId == Guid.Empty)
        {
            throw new ArgumentException("A Payment identifier is required.", nameof(paymentId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(currency.Value);

        return SendPaymentAsync(
            () =>
            {
                HttpRequestMessage message = CreateRequest(
                    HttpMethod.Put,
                    $"payments/{paymentId:D}/currency-selection");
                message.Content = JsonContent.Create(
                    new SelectCurrencyRequest(currency),
                    options: _jsonOptions);
                return message;
            },
            cancellationToken);
    }

    /// <summary>
    /// Simulates the Payer paying a Payment of a Test Mode installation, for
    /// testing an integration end to end without real funds.
    /// </summary>
    /// <param name="amount">
    /// The amount sent, as a decimal in the selected currency; <c>null</c> pays
    /// exactly the expected amount. Less exercises an underpayment, more an
    /// overpayment, and a second call tops up.
    /// </param>
    /// <param name="idempotencyKey">
    /// Makes a retry return the first simulated transaction instead of sending
    /// another. Without one, the SDK does not retry a request whose outcome it
    /// cannot know, because a second transaction would be an overpayment.
    /// </param>
    /// <exception cref="PayaffeApiException">
    /// With <see cref="PayaffeErrorCode.TestModeUnavailable"/> when the
    /// installation is live and has no such route.
    /// </exception>
    public async Task<SimulatedTransaction> SimulatePaymentAsync(
        Guid paymentId,
        string? amount = null,
        string? idempotencyKey = null,
        CancellationToken cancellationToken = default)
    {
        if (paymentId == Guid.Empty)
        {
            throw new ArgumentException("A Payment identifier is required.", nameof(paymentId));
        }

        try
        {
            return await SendAsync<SimulatedTransaction>(
                () =>
                {
                    HttpRequestMessage message = CreateRequest(
                        HttpMethod.Post,
                        $"payments/{paymentId:D}/simulated-transactions");
                    if (!string.IsNullOrWhiteSpace(idempotencyKey))
                    {
                        message.Headers.Add("Idempotency-Key", idempotencyKey);
                    }

                    message.Content = JsonContent.Create(new SimulatePaymentRequest(amount), options: _jsonOptions);
                    return message;
                },
                retryAmbiguousFailures: !string.IsNullOrWhiteSpace(idempotencyKey),
                cancellationToken).ConfigureAwait(false);
        }
        catch (PayaffeApiException exception) when (
            exception.StatusCode == HttpStatusCode.NotFound &&
            exception.Code == PayaffeErrorCode.UnexpectedError)
        {
            // A live installation answers an unknown route with a bare 404, not
            // with the problem body a missing Payment gets.
            throw new PayaffeApiException(
                HttpStatusCode.NotFound,
                PayaffeErrorCode.TestModeUnavailable,
                exception.CorrelationId,
                exception.ValidationErrors,
                retryAfter: null,
                "This installation is not in Test Mode, so a payment cannot be simulated.");
        }
    }

    /// <summary>
    /// Reads Payment state until it is terminal or the caller cancels.
    /// </summary>
    /// <remarks>
    /// Polling is the reconciliation path, so a transient failure does not end
    /// it: a transport failure, a timeout, or a 429, 502, 503 or 504 that
    /// outlasted the request's own retries is skipped, and the next read waits
    /// the grown interval, or longer when the server sent <c>Retry-After</c>.
    /// Any other API error ends polling with a <see cref="PayaffeApiException"/>.
    /// </remarks>
    public async IAsyncEnumerable<Payment> PollPaymentAsync(
        Guid paymentId,
        PaymentPollingOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        PaymentPollingOptions pollingOptions = options ?? new PaymentPollingOptions();
        pollingOptions.Validate();
        TimeSpan interval = pollingOptions.InitialInterval;

        while (true)
        {
            Payment? payment = null;
            TimeSpan? retryAfter = null;
            try
            {
                payment = await GetPaymentAsync(paymentId, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception exception) when (IsTransientPollingFailure(exception, cancellationToken))
            {
                retryAfter = (exception as PayaffeApiException)?.RetryAfter;
            }

            if (payment is not null)
            {
                yield return payment;

                if (payment.Status.IsTerminal)
                {
                    yield break;
                }
            }

            TimeSpan delay = ApplyJitter(interval, pollingOptions.JitterRatio);
            await Task.Delay(retryAfter > delay ? retryAfter.Value : delay, cancellationToken)
                .ConfigureAwait(false);
            interval = DoubleAndCap(interval, pollingOptions.MaximumInterval);
        }
    }

    internal static Uri ValidateBaseAddress(Uri? baseAddress)
    {
        ArgumentNullException.ThrowIfNull(baseAddress);
        if (!baseAddress.IsAbsoluteUri ||
            (baseAddress.Scheme != Uri.UriSchemeHttp && baseAddress.Scheme != Uri.UriSchemeHttps) ||
            !string.IsNullOrEmpty(baseAddress.Query) ||
            !string.IsNullOrEmpty(baseAddress.Fragment) ||
            !string.IsNullOrEmpty(baseAddress.UserInfo))
        {
            throw new ArgumentException(
                "The Payaffe base address must be an absolute HTTP or HTTPS URL without " +
                "credentials, a query, or a fragment.",
                nameof(baseAddress));
        }

        string value = baseAddress.AbsoluteUri.EndsWith("/", StringComparison.Ordinal)
            ? baseAddress.AbsoluteUri
            : $"{baseAddress.AbsoluteUri}/";
        return new Uri(value, UriKind.Absolute);
    }

    internal static string ValidateApiToken(string? apiToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(apiToken);
        _ = new AuthenticationHeaderValue("Bearer", apiToken);
        return apiToken;
    }

    private Task<Payment> SendPaymentAsync(
        Func<HttpRequestMessage> requestFactory,
        CancellationToken cancellationToken) =>
        SendAsync<Payment>(requestFactory, retryAmbiguousFailures: true, cancellationToken);

    /// <param name="retryAmbiguousFailures">
    /// Whether a transport failure or timeout may be retried. Those leave the
    /// outcome unknown, so only a request the server deduplicates may repeat.
    /// A 429 or 503 is always retried: the request was not processed.
    /// </param>
    private async Task<T> SendAsync<T>(
        Func<HttpRequestMessage> requestFactory,
        bool retryAmbiguousFailures,
        CancellationToken cancellationToken)
        where T : class
    {
        int maximumAmbiguousRetries = retryAmbiguousFailures ? _options.MaximumRetries : 0;
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                using HttpRequestMessage request = requestFactory();
                using HttpResponseMessage response = await _httpClient.SendAsync(
                    request,
                    HttpCompletionOption.ResponseHeadersRead,
                    cancellationToken).ConfigureAwait(false);

                TimeSpan? retryAfter = GetRetryAfter(response);
                // A Retry-After beyond the retry delay bound is handed to the
                // caller in the exception rather than blocking the call.
                if (ShouldRetry(response.StatusCode) &&
                    attempt < _options.MaximumRetries &&
                    !(retryAfter > _options.MaximumRetryDelay))
                {
                    await DelayBeforeRetryAsync(attempt, retryAfter, cancellationToken)
                        .ConfigureAwait(false);
                    continue;
                }

                if (!response.IsSuccessStatusCode)
                {
                    throw await CreateApiExceptionAsync(response, retryAfter, cancellationToken)
                        .ConfigureAwait(false);
                }

                T? result = await response.Content.ReadFromJsonAsync<T>(
                    _jsonOptions,
                    cancellationToken).ConfigureAwait(false);
                return result ?? throw new JsonException($"Payaffe returned an empty {typeof(T).Name} response.");
            }
            catch (HttpRequestException) when (attempt < maximumAmbiguousRetries)
            {
                await DelayBeforeRetryAsync(attempt, null, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (
                !cancellationToken.IsCancellationRequested &&
                attempt < maximumAmbiguousRetries)
            {
                await DelayBeforeRetryAsync(attempt, null, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string relativePath)
    {
        HttpRequestMessage request = new(
            method,
            new Uri(_options.BaseAddress, $"api/v1/{relativePath}"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }

    private async Task DelayBeforeRetryAsync(
        int attempt,
        TimeSpan? retryAfter,
        CancellationToken cancellationToken)
    {
        TimeSpan exponentialDelay = _options.InitialRetryDelay;
        for (int index = 0; index < attempt; index++)
        {
            exponentialDelay = DoubleAndCap(exponentialDelay, _options.MaximumRetryDelay);
        }

        exponentialDelay = ApplyJitter(exponentialDelay, _options.RetryJitterRatio);
        TimeSpan delay = retryAfter > exponentialDelay ? retryAfter.Value : exponentialDelay;
        await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
    }

    private static bool ShouldRetry(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable;

    private static bool IsTransientPollingFailure(
        Exception exception,
        CancellationToken cancellationToken) =>
        exception switch
        {
            HttpRequestException => true,
            OperationCanceledException => !cancellationToken.IsCancellationRequested,
            PayaffeApiException apiException => apiException.StatusCode is
                HttpStatusCode.TooManyRequests or
                HttpStatusCode.BadGateway or
                HttpStatusCode.ServiceUnavailable or
                HttpStatusCode.GatewayTimeout,
            _ => false,
        };

    private static TimeSpan DoubleAndCap(TimeSpan value, TimeSpan maximum)
    {
        if (value >= maximum || value.Ticks > maximum.Ticks / 2)
        {
            return maximum;
        }

        return TimeSpan.FromTicks(value.Ticks * 2);
    }

    private static TimeSpan ApplyJitter(TimeSpan value, double ratio)
    {
        if (ratio == 0)
        {
            return value;
        }

        double multiplier = 1 + ((Random.Shared.NextDouble() * 2 - 1) * ratio);
        return TimeSpan.FromTicks(Math.Max(1, (long)(value.Ticks * multiplier)));
    }

    private static TimeSpan? GetRetryAfter(HttpResponseMessage response)
    {
        RetryConditionHeaderValue? retryAfter = response.Headers.RetryAfter;
        if (retryAfter?.Delta is TimeSpan delta)
        {
            return delta < TimeSpan.Zero ? TimeSpan.Zero : delta;
        }

        if (retryAfter?.Date is DateTimeOffset date)
        {
            TimeSpan remaining = date - DateTimeOffset.UtcNow;
            return remaining < TimeSpan.Zero ? TimeSpan.Zero : remaining;
        }

        return null;
    }

    private static async Task<PayaffeApiException> CreateApiExceptionAsync(
        HttpResponseMessage response,
        TimeSpan? retryAfter,
        CancellationToken cancellationToken)
    {
        IntegrationApiProblem? problem = null;
        try
        {
            problem = await response.Content.ReadFromJsonAsync<IntegrationApiProblem>(
                _jsonOptions,
                cancellationToken).ConfigureAwait(false);
        }
        catch (JsonException)
        {
            // An invalid error body remains an API error with a stable fallback code.
        }

        Dictionary<string, IReadOnlyList<string>> validationErrors =
            problem?.Errors?.ToDictionary(
                pair => pair.Key,
                pair => (IReadOnlyList<string>)pair.Value,
                StringComparer.Ordinal) ?? new Dictionary<string, IReadOnlyList<string>>();

        return new PayaffeApiException(
            response.StatusCode,
            string.IsNullOrWhiteSpace(problem?.Code)
                ? PayaffeErrorCode.UnexpectedError
                : new PayaffeErrorCode(problem.Code),
            problem?.CorrelationId,
            validationErrors,
            retryAfter,
            problem?.Title);
    }

    private sealed record SelectCurrencyRequest(SupportedCurrency SupportedCurrency);

    private sealed record SimulatePaymentRequest(string? Amount);

    private sealed class IntegrationApiProblem
    {
        public string? Title { get; init; }

        public string? Code { get; init; }

        public string? CorrelationId { get; init; }

        public Dictionary<string, string[]>? Errors { get; init; }
    }
}
