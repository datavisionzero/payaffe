using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace EmbeddedShop.Tests;

/// <summary>
/// A Payaffe installation, only far enough to answer the three Integration API calls the shop
/// makes and to hold the Payments it created. It is scoped by bearer token, so a request made
/// with one storefront's credential cannot see the other storefront's Payments — which is the
/// property the real API has and the one the isolation test relies on.
/// </summary>
internal sealed class FakePayaffe : HttpMessageHandler
{
    public const string TeahouseToken = "teahouse-integration-token";
    public const string RoasteryToken = "roastery-integration-token";

    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);
    private readonly ConcurrentDictionary<Guid, StoredPayment> _payments = new();

    /// <summary>Every request the shop made, so a test can show the browser made none of them.</summary>
    public ConcurrentQueue<RecordedRequest> Requests { get; } = new();

    /// <summary>The next Currency Selection fails with this stable code, once.</summary>
    public string? NextSelectionFailure { get; set; }

    /// <summary>A Payment that a later read reports as completed, standing in for a transfer.</summary>
    public HashSet<Guid> CompletedPayments { get; } = [];

    public HashSet<Guid> ExpiredPayments { get; } = [];

    /// <summary>
    /// How many of the next creations succeed and then lose their answer, which is what a timeout
    /// after the server committed looks like to the shop.
    /// </summary>
    public int LostCreationAnswers { get; set; }

    /// <summary>Stand-ins for the next Payment reads, one per read, in order.</summary>
    public ConcurrentQueue<Func<HttpResponseMessage>> ReadFaults { get; } = new();

    /// <summary>Creates a Payment in a storefront's Project directly, as another integration would.</summary>
    public StoredPayment CreateElsewhere(string token, string externalReference, long fiatAmountMinor)
    {
        StoredPayment created = new()
        {
            PaymentId = Guid.CreateVersion7(),
            Token = token,
            ExternalReference = externalReference,
            FiatCurrency = "EUR",
            FiatAmountMinor = fiatAmountMinor,
        };
        _payments[created.PaymentId] = created;
        return created;
    }

    public StoredPayment PaymentFor(string externalReference) =>
        _payments.Values.Single(payment => payment.ExternalReference == externalReference);

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        string token = request.Headers.Authorization?.Parameter ?? string.Empty;
        string path = request.RequestUri!.AbsolutePath;
        Requests.Enqueue(new RecordedRequest(request.Method.Method, path, token));

        if (request.Method == HttpMethod.Post && path == "/api/v1/payments")
        {
            HttpResponseMessage created = await CreateAsync(request, token, cancellationToken);
            if (LostCreationAnswers > 0)
            {
                LostCreationAnswers--;
                created.Dispose();
                throw new TaskCanceledException("The answer to the creation was lost.");
            }

            return created;
        }

        if (request.Method == HttpMethod.Get && TryReadPaymentId(path, "", out Guid readId))
        {
            if (ReadFaults.TryDequeue(out Func<HttpResponseMessage>? fault))
            {
                return fault();
            }

            return Find(readId, token) is { } payment
                ? Json(HttpStatusCode.OK, Project(payment))
                : Problem(HttpStatusCode.NotFound, "payment.not_found");
        }

        if (request.Method == HttpMethod.Put &&
            TryReadPaymentId(path, "/currency-selection", out Guid selectionId))
        {
            return await SelectAsync(request, selectionId, token, cancellationToken);
        }

        return Problem(HttpStatusCode.NotFound, "route.not_found");
    }

    private async Task<HttpResponseMessage> CreateAsync(
        HttpRequestMessage request,
        string token,
        CancellationToken cancellationToken)
    {
        JsonElement body = await request.Content!.ReadFromJsonAsync<JsonElement>(cancellationToken);
        string externalReference = body.GetProperty("externalReference").GetString()!;

        // The Idempotency-Key replays rather than creating a second Payment, as the contract says.
        StoredPayment? existing = _payments.Values.FirstOrDefault(payment =>
            payment.Token == token && payment.ExternalReference == externalReference);
        if (existing is not null)
        {
            return Json(HttpStatusCode.OK, Project(existing));
        }

        StoredPayment created = new()
        {
            PaymentId = Guid.CreateVersion7(),
            Token = token,
            ExternalReference = externalReference,
            FiatCurrency = body.GetProperty("fiatCurrency").GetString()!,
            FiatAmountMinor = body.GetProperty("fiatAmountMinor").GetInt64(),
        };
        _payments[created.PaymentId] = created;
        return Json(HttpStatusCode.Created, Project(created));
    }

    private async Task<HttpResponseMessage> SelectAsync(
        HttpRequestMessage request,
        Guid paymentId,
        string token,
        CancellationToken cancellationToken)
    {
        StoredPayment? payment = Find(paymentId, token);
        if (payment is null)
        {
            return Problem(HttpStatusCode.NotFound, "payment.not_found");
        }

        if (NextSelectionFailure is { } failure)
        {
            NextSelectionFailure = null;
            return Problem(HttpStatusCode.Conflict, failure);
        }

        JsonElement body = await request.Content!.ReadFromJsonAsync<JsonElement>(cancellationToken);
        string currency = body.GetProperty("supportedCurrency").GetString()!.ToUpperInvariant();

        if (payment.SelectedCurrency is not null && payment.SelectedCurrency != currency)
        {
            return Problem(HttpStatusCode.Conflict, "payment.currency_already_selected");
        }

        payment.SelectedCurrency = currency;
        payment.Status = "waiting_for_payment";
        return Json(HttpStatusCode.OK, Project(payment));
    }

    private StoredPayment? Find(Guid paymentId, string token) =>
        _payments.TryGetValue(paymentId, out StoredPayment? payment) && payment.Token == token
            ? payment
            : null;

    private static bool TryReadPaymentId(string path, string suffix, out Guid paymentId)
    {
        paymentId = Guid.Empty;
        const string Prefix = "/api/v1/payments/";
        if (!path.StartsWith(Prefix, StringComparison.Ordinal) ||
            !path.EndsWith(suffix, StringComparison.Ordinal))
        {
            return false;
        }

        string candidate = path[Prefix.Length..^suffix.Length];
        return Guid.TryParse(candidate, out paymentId);
    }

    private object Project(StoredPayment payment)
    {
        string status = CompletedPayments.Contains(payment.PaymentId)
            ? "completed"
            : ExpiredPayments.Contains(payment.PaymentId)
                ? "expired"
                : payment.Status;
        DateTimeOffset now = DateTimeOffset.UtcNow;

        return new
        {
            paymentId = payment.PaymentId,
            status,
            payerPageUrl = $"https://payaffe.test/pay/{payment.PaymentId:N}",
            expiresAt = now.AddMinutes(30),
            fiatCurrency = payment.FiatCurrency,
            fiatAmountMinor = payment.FiatAmountMinor,
            externalReference = payment.ExternalReference,
            selectedCurrency = payment.SelectedCurrency,
            expectedCryptoAmount = payment.SelectedCurrency is null ? null : "0.00039980",
            paymentAddress = payment.SelectedCurrency is null ? null : PaymentAddress,
            observedTotal = (string?)null,
            completedAt = status == "completed" ? now : (DateTimeOffset?)null,
            settledAt = (DateTimeOffset?)null,
            returnUrl = (string?)null,
            paymentOptions = new[]
            {
                new { supportedCurrency = "BTC", status = "available", unavailableReasonCode = (string?)null, checkedAt = now },
                new { supportedCurrency = "LTC", status = "available", unavailableReasonCode = (string?)null, checkedAt = now },
                new { supportedCurrency = "ETH", status = "unavailable", unavailableReasonCode = (string?)"no_address_available", checkedAt = now },
            },
            createdAt = now,
            updatedAt = now,
            lateAcceptanceEndsAt = now.AddHours(24),
            confirmedEligibleTotal = (string?)null,
            observedAmountState = (string?)null,
            rateLock = payment.SelectedCurrency is null
                ? null
                : new
                {
                    fiatCurrency = payment.FiatCurrency,
                    fiatAmountMinor = payment.FiatAmountMinor,
                    supportedCurrency = payment.SelectedCurrency,
                    expectedCryptoAmount = "0.00039980",
                    expectedCryptoAmountAtomic = "39980",
                    fiatPerCryptoUnit = "32000.00",
                    source = "test",
                    rateObservedAt = now,
                    lockedAt = now,
                    validUntil = now.AddMinutes(30),
                },
            paymentInstruction = payment.SelectedCurrency is null
                ? null
                : new
                {
                    supportedCurrency = payment.SelectedCurrency,
                    network = "mainnet",
                    chainId = (long?)null,
                    amount = "0.00039980",
                    amountAtomic = "39980",
                    paymentAddress = PaymentAddress,
                    uri = PaymentUri,
                    expiresAt = now.AddMinutes(30),
                },
        };
    }

    public const string PaymentAddress = "bc1qexampleshopaddress0000000000000000000000000";

    public const string PaymentUri =
        "bitcoin:bc1qexampleshopaddress0000000000000000000000000?amount=0.00039980";

    private static HttpResponseMessage Json(HttpStatusCode statusCode, object body) =>
        new(statusCode) { Content = JsonContent.Create(body, options: _json) };

    private static HttpResponseMessage Problem(HttpStatusCode statusCode, string code) =>
        new(statusCode)
        {
            Content = JsonContent.Create(
                new { type = "about:blank", title = code, status = (int)statusCode, code },
                options: _json),
        };

    internal sealed class StoredPayment
    {
        public required Guid PaymentId { get; init; }

        public required string Token { get; init; }

        public required string ExternalReference { get; init; }

        public required string FiatCurrency { get; init; }

        public required long FiatAmountMinor { get; init; }

        public string Status { get; set; } = "pending_currency_selection";

        public string? SelectedCurrency { get; set; }
    }

    internal sealed record RecordedRequest(string Method, string Path, string Token);
}
