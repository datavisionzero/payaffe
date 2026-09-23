using System.Collections.Concurrent;
using Payaffe.Sdk;

namespace EmbeddedShop;

public enum FulfillmentState
{
    /// <summary>The order exists and nothing has been handed over.</summary>
    AwaitingPayment,

    /// <summary>Payaffe reported `completed` or `settled` and the goods went out, once.</summary>
    Fulfilled,

    /// <summary>
    /// The Payment expired without an authoritative completion. Not a failure of the order: a
    /// late transfer can still complete or be settled manually, so the order stays readable and
    /// the reconciler keeps listening.
    /// </summary>
    Expired,
}

/// <summary>
/// One order of this shop. The shop owns the customer relationship, the price and the decision
/// to hand the goods over; Payaffe owns whether the money arrived. The Payment identifier is
/// stored here, next to the order, which is what makes reconciliation possible later.
/// </summary>
public sealed class ShopOrder
{
    private readonly Lock _gate = new();

    public required string Storefront { get; init; }

    public required Guid OrderId { get; init; }

    /// <summary>
    /// The shop's own customer. A browser that presents another customer's order identifier is
    /// not that customer, which is why every read is checked against this value.
    /// </summary>
    public required string CustomerId { get; init; }

    public required ShopItem Item { get; init; }

    public required string FiatCurrency { get; init; }

    /// <summary>
    /// Whether this order's storefront lets a Test Mode Payment fulfil it. See
    /// <see cref="StorefrontOptions.AcceptTestPayments"/>.
    /// </summary>
    public bool AcceptsTestPayments { get; init; }

    /// <summary>Whether the Payment behind this order is a simulated one.</summary>
    public bool TestMode { get; private set; }

    public Guid? PaymentId { get; private set; }

    public PaymentStatus? Status { get; private set; }

    public SupportedCurrency? SelectedCurrency { get; private set; }

    public PaymentInstruction? Instruction { get; private set; }

    public IReadOnlyList<PaymentOption> Options { get; private set; } = [];

    public DateTimeOffset? ExpiresAt { get; private set; }

    public FulfillmentState Fulfillment { get; private set; } = FulfillmentState.AwaitingPayment;

    public DateTimeOffset? FulfilledAt { get; private set; }

    /// <summary>
    /// How often the shop decided to hand the goods over. It exists because the only honest way
    /// to show that a duplicate Delivery changes nothing is to count the times it could have.
    /// </summary>
    public int FulfillmentCount { get; private set; }

    public string? LastSignal { get; private set; }

    /// <summary>
    /// Binds the Payment Payaffe created for this order. The first binding is the only one: a
    /// retried creation with the order's idempotency key returns the same Payment and binds
    /// again harmlessly, while a Payment for another amount or currency, or a second Payment,
    /// is refused and changes nothing.
    /// </summary>
    public bool AttachPayment(Payment payment)
    {
        ArgumentNullException.ThrowIfNull(payment);
        lock (_gate)
        {
            if (!IsForThisOrder(payment.FiatCurrency, payment.FiatAmountMinor) ||
                (PaymentId is not null && PaymentId != payment.PaymentId))
            {
                return false;
            }

            PaymentId = payment.PaymentId;
        }

        return Apply(payment, "created");
    }

    /// <summary>
    /// Whether a Payment is the one this order is paid by. The External Reference only says
    /// which order a Payment claims to be for, and anything in the Project can claim that; the
    /// Payment identifier the shop stored at creation, the amount and the currency are what make
    /// it this order's Payment.
    /// </summary>
    public bool Describes(Guid paymentId, string fiatCurrency, long fiatAmountMinor)
    {
        lock (_gate)
        {
            return PaymentId == paymentId && IsForThisOrder(fiatCurrency, fiatAmountMinor);
        }
    }

    /// <summary>
    /// Folds the Payment as the shop just read it into the order. Returns false, changing
    /// nothing, when it is not this order's Payment.
    /// </summary>
    public bool Apply(Payment payment, string signal)
    {
        ArgumentNullException.ThrowIfNull(payment);
        return ApplyCore(
            payment.PaymentId,
            payment.FiatCurrency,
            payment.FiatAmountMinor,
            payment.Status,
            payment.SelectedCurrency,
            payment.ExpiresAt,
            payment.PaymentInstruction,
            payment.PaymentOptions,
            payment.TestMode,
            signal);
    }

    /// <summary>
    /// Folds the Payment a verified Webhook Event describes into the order. The Delivery carries
    /// no Payment Instruction and no options, so what the order already has is kept. Returns
    /// false, changing nothing, when it is not this order's Payment.
    /// </summary>
    public bool Apply(PayaffeWebhookPayment payment, bool testMode, string signal)
    {
        ArgumentNullException.ThrowIfNull(payment);
        return ApplyCore(
            payment.PaymentId,
            payment.FiatCurrency,
            payment.FiatAmountMinor,
            payment.Status,
            payment.SelectedCurrency,
            payment.ExpiresAt,
            instruction: null,
            options: null,
            testMode,
            signal);
    }

    private bool IsForThisOrder(string fiatCurrency, long fiatAmountMinor) =>
        string.Equals(fiatCurrency, FiatCurrency, StringComparison.Ordinal) &&
        fiatAmountMinor == Item.PriceMinor;

    /// <summary>
    /// The one place an authoritative Payment state reaches the order. The Webhook endpoint and
    /// the reconciling poller both land here, because they carry the same statement and the
    /// order must not care which arrived first. A fulfilled order absorbs later states without
    /// acting on them again, so an out-of-order or repeated Delivery cannot fulfil it twice or
    /// take a fulfilment back.
    /// </summary>
    private bool ApplyCore(
        Guid paymentId,
        string fiatCurrency,
        long fiatAmountMinor,
        PaymentStatus status,
        SupportedCurrency? selectedCurrency,
        DateTimeOffset expiresAt,
        PaymentInstruction? instruction,
        IReadOnlyList<PaymentOption>? options,
        bool testMode,
        string signal)
    {
        lock (_gate)
        {
            if (PaymentId != paymentId || !IsForThisOrder(fiatCurrency, fiatAmountMinor))
            {
                return false;
            }

            LastSignal = signal;
            TestMode = testMode;
            ExpiresAt = expiresAt;
            if (options is not null)
            {
                Options = options;
            }

            if (selectedCurrency is not null)
            {
                SelectedCurrency = selectedCurrency;
            }

            if (instruction is not null)
            {
                Instruction = instruction;
            }

            if (Fulfillment == FulfillmentState.Fulfilled)
            {
                return true;
            }

            Status = status;

            // A simulated Payment is shown for what it is and never hands goods over, unless
            // this storefront was set up to be tested against a Test Mode installation.
            if (testMode && !AcceptsTestPayments)
            {
                LastSignal = $"{signal}, ignored: simulated payment";
                return true;
            }

            if (status == PaymentStatus.Completed || status == PaymentStatus.Settled)
            {
                Fulfillment = FulfillmentState.Fulfilled;
                FulfilledAt = DateTimeOffset.UtcNow;
                FulfillmentCount++;
                return true;
            }

            if (status == PaymentStatus.Expired)
            {
                Fulfillment = FulfillmentState.Expired;
            }

            return true;
        }
    }
}

/// <summary>
/// An in-memory order book, which is the one place this sample is deliberately not a product:
/// a real shop keeps orders and the identifiers of the Webhook Events it has already seen in
/// its database, in the same transaction that fulfils the order.
/// </summary>
public sealed class OrderStore
{
    private readonly ConcurrentDictionary<Guid, ShopOrder> _orders = new();
    private readonly ConcurrentDictionary<(string Storefront, Guid EventId), byte> _handledEvents = new();

    public ShopOrder Create(
        string storefront,
        string customerId,
        ShopItem item,
        string fiatCurrency,
        bool acceptsTestPayments = false)
    {
        ShopOrder order = new()
        {
            Storefront = storefront,
            OrderId = Guid.CreateVersion7(),
            CustomerId = customerId,
            Item = item,
            FiatCurrency = fiatCurrency,
            AcceptsTestPayments = acceptsTestPayments,
        };

        _orders[order.OrderId] = order;
        return order;
    }

    /// <summary>
    /// Finds an order the given customer of the given storefront owns. A mismatch is not found
    /// rather than forbidden, so an identifier cannot be probed for existence.
    /// </summary>
    public ShopOrder? FindForCustomer(string storefront, Guid orderId, string customerId) =>
        _orders.TryGetValue(orderId, out ShopOrder? order) &&
        string.Equals(order.Storefront, storefront, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(order.CustomerId, customerId, StringComparison.Ordinal)
            ? order
            : null;

    /// <summary>
    /// Finds the order a verified Webhook Event or a reconciling read refers to. The External
    /// Reference is how Payaffe names the order back to the shop, and it is only ever read
    /// inside the storefront whose credential and secret produced it.
    /// </summary>
    public ShopOrder? FindByExternalReference(string storefront, string externalReference) =>
        Guid.TryParse(externalReference, out Guid orderId) &&
        _orders.TryGetValue(orderId, out ShopOrder? order) &&
        string.Equals(order.Storefront, storefront, StringComparison.OrdinalIgnoreCase)
            ? order
            : null;

    public IReadOnlyList<ShopOrder> AwaitingPayment() =>
        [.. _orders.Values.Where(order =>
            order.Fulfillment == FulfillmentState.AwaitingPayment && order.PaymentId is not null)];

    /// <summary>
    /// Records that one Webhook Event has been taken. Returns false for a Delivery that was
    /// already handled, which is what stops an at-least-once channel from fulfilling twice.
    /// </summary>
    public bool TryClaimEvent(string storefront, Guid eventId) =>
        _handledEvents.TryAdd((storefront, eventId), 0);
}
