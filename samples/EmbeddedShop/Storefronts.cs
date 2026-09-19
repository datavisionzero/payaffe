namespace EmbeddedShop;

/// <summary>
/// The sample runs two storefronts side by side. They are separate products as far as this
/// application is concerned: separate Payaffe Projects, separate Integration API credentials,
/// separate Webhook endpoints and secrets, and separate orders. Nothing an order carries lets
/// one storefront reach into the other, which is the property the isolation tests pin.
/// </summary>
public sealed class ShopOptions
{
    public Dictionary<string, StorefrontOptions> Storefronts { get; init; } =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Whether the background reconciler polls Payaffe for orders that are waiting. A product
    /// keeps this on: a Webhook Delivery can be lost, and polling is the channel that notices.
    /// </summary>
    public bool ReconcileByPolling { get; init; } = true;
}

public sealed class StorefrontOptions
{
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>
    /// The Payaffe installation this storefront's Project lives in. It is used by this backend
    /// only; the browser never learns it.
    /// </summary>
    public Uri? PayaffeBaseAddress { get; init; }

    /// <summary>
    /// The Integration API credential of one Payaffe Project. Read from configuration, which in
    /// a deployment means a secret store rather than a file in the repository.
    /// </summary>
    public string PayaffeApiToken { get; init; } = string.Empty;

    /// <summary>
    /// The shared secret of this storefront's Webhook endpoint. A Delivery signed with another
    /// storefront's secret does not verify here, which is the point of not sharing one.
    /// </summary>
    public string WebhookSecret { get; init; } = string.Empty;

    public string FiatCurrency { get; init; } = "EUR";
}

/// <summary>
/// One purchasable thing, so the sample has an order worth paying for.
/// </summary>
public sealed record ShopItem(string Sku, string Name, long PriceMinor);

public static class ShopCatalog
{
    public static IReadOnlyList<ShopItem> Items { get; } =
    [
        new ShopItem("tea-100", "Loose leaf tea, 100 g", 1299),
        new ShopItem("mug-01", "Stoneware mug", 1999),
        new ShopItem("kettle-1l", "Cast iron kettle, 1 l", 8450),
    ];

    public static ShopItem? Find(string sku) =>
        Items.FirstOrDefault(item => string.Equals(item.Sku, sku, StringComparison.Ordinal));
}
