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
            Payment payment = await GetPaymentAsync(paymentId, cancellationToken)
                .ConfigureAwait(false);
            yield return payment;

            if (payment.Status.IsTerminal)
            {
                yield break;
            }

            await Task.Delay(ApplyJitter(interval, pollingOptions.JitterRatio), cancellationToken)
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

    private async Task<Payment> SendPaymentAsync(
        Func<HttpRequestMessage> requestFactory,
        CancellationToken cancellationToken)
    {
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
                if (ShouldRetry(response.StatusCode) && attempt < _options.MaximumRetries)
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

                Payment? payment = await response.Content.ReadFromJsonAsync<Payment>(
                    _jsonOptions,
                    cancellationToken).ConfigureAwait(false);
                return payment ?? throw new JsonException("Payaffe returned an empty Payment response.");
            }
            catch (HttpRequestException) when (attempt < _options.MaximumRetries)
            {
                await DelayBeforeRetryAsync(attempt, null, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (
                !cancellationToken.IsCancellationRequested &&
                attempt < _options.MaximumRetries)
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

    private sealed class IntegrationApiProblem
    {
        public string? Title { get; init; }

        public string? Code { get; init; }

        public string? CorrelationId { get; init; }

        public Dictionary<string, string[]>? Errors { get; init; }
    }
}
