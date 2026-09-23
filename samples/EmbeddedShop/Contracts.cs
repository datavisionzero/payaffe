namespace EmbeddedShop;

internal sealed record CreateOrderRequest(string? Sku);

internal sealed record SelectCurrencyBody(string? Currency);

internal sealed record SimulatePaymentBody(string? Amount);

internal static class ShopCustomer
{
    public const string CookieName = "embedded-shop-customer";

    public const string ItemKey = "shop-customer";

    public static string Of(HttpContext context) => (string)context.Items[ItemKey]!;
}

/// <summary>
/// What this shop tells its own browser about an order. It is the shop's shape, not Payaffe's:
/// no bearer token, no Payer Page URL, no Payaffe origin, and no field the page does not need.
/// Any frontend — this page, a mobile app, a React storefront — consumes exactly this, which is
/// why adopting the SDK does not constrain what a product's frontend is built with.
/// </summary>
internal sealed record OrderView(
    Guid OrderId,
    string Item,
    long AmountMinor,
    string FiatCurrency,
    string PaymentState,
    string Fulfillment,
    string? SelectedCurrency,
    IReadOnlyList<OrderCurrencyOption> Options,
    OrderInstructionView? Instruction,
    DateTimeOffset? ExpiresAt,
    string? LastSignal,
    bool TestMode,
    bool CanSimulatePayment)
{
    public static OrderView Of(ShopOrder order)
    {
        ArgumentNullException.ThrowIfNull(order);
        return new OrderView(
            order.OrderId,
            order.Item.Name,
            order.Item.PriceMinor,
            order.FiatCurrency,
            order.Status?.Value ?? "none",
            order.Fulfillment.ToString(),
            order.SelectedCurrency?.Value,
            [.. order.Options.Select(option => new OrderCurrencyOption(
                option.SupportedCurrency.Value,
                option.Status.Value,
                option.UnavailableReasonCode))],
            order.Instruction is null
                ? null
                : new OrderInstructionView(
                    order.Instruction.SupportedCurrency.Value,
                    order.Instruction.Amount,
                    order.Instruction.PaymentAddress,
                    order.Instruction.Uri,
                    order.Instruction.ExpiresAt,
                    $"/{order.Storefront}/api/orders/{order.OrderId:D}/payment-code.svg"),
            order.ExpiresAt,
            order.LastSignal,
            order.TestMode,
            order.TestMode &&
                order.AcceptsTestPayments &&
                order.Instruction is not null &&
                order.Fulfillment == FulfillmentState.AwaitingPayment);
    }
}

internal sealed record OrderCurrencyOption(string Currency, string Status, string? UnavailableReason);

/// <summary>
/// The Payment Instruction as the page needs it. `WalletUri` is here because a payer on a phone
/// taps it to open their own wallet, which is a user-initiated navigation to a wallet, not a
/// request to a Payaffe origin. `QrCodeUrl` points back at this shop.
/// </summary>
internal sealed record OrderInstructionView(
    string Currency,
    string Amount,
    string Address,
    string WalletUri,
    DateTimeOffset ExpiresAt,
    string QrCodeUrl);
