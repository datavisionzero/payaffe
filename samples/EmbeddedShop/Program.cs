using EmbeddedShop;
using Microsoft.Extensions.Options;
using Payaffe.Sdk;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddOptions<ShopOptions>()
    .Bind(builder.Configuration.GetSection("Shop"))
    .Validate(
        options => options.Storefronts.Count > 0,
        "The sample needs at least one configured storefront.")
    .Validate(
        options => options.Storefronts.Values.All(storefront =>
            storefront.PayaffeBaseAddress is not null &&
            !string.IsNullOrWhiteSpace(storefront.PayaffeApiToken) &&
            !string.IsNullOrWhiteSpace(storefront.WebhookSecret)),
        "Every storefront needs a Payaffe base address, an Integration API token and a webhook " +
        "secret. See samples/EmbeddedShop/README.md; the repository ships none of them.")
    .ValidateOnStart();

builder.Services.AddSingleton<OrderStore>();
builder.Services.AddSingleton<ShopPayments>();
builder.Services.AddSingleton<OrderReconciler>();
builder.Services.AddHostedService(serviceProvider =>
    serviceProvider.GetRequiredService<OrderReconciler>());

// One Payaffe client per storefront, each with its own Project credential. Two storefronts can
// therefore never spend each other's addresses or read each other's Payments, and rotating one
// credential does not touch the other.
ShopOptions shopOptions = builder.Configuration.GetSection("Shop").Get<ShopOptions>()
    ?? new ShopOptions();
foreach ((string key, StorefrontOptions storefront) in shopOptions.Storefronts)
{
    builder.Services.AddPayaffeClient(key, options =>
    {
        options.BaseAddress = storefront.PayaffeBaseAddress;
        options.ApiToken = storefront.PayaffeApiToken;
    });
}

WebApplication app = builder.Build();

// The shop's own stylesheet and script, and nothing else. They live under /assets because the
// storefront routes below claim the first path segment: a file served from the root would be
// matched by "/{storefront}/" first, and a request that has an endpoint never reaches the static
// file middleware. The checkout page itself is served by the storefront route, so there is no
// path at which it renders without knowing which storefront it belongs to.
app.UseStaticFiles();

string checkoutPage = File.ReadAllText(
    Path.Combine(app.Environment.ContentRootPath, "Pages", "checkout.html"));

// The shop's own customer. A real product has accounts and a session; the sample needs only the
// part that matters here, which is that an order belongs to somebody and a browser that presents
// another customer's order identifier is still not that customer.
app.Use(async (context, next) =>
{
    if (!context.Request.Cookies.TryGetValue(ShopCustomer.CookieName, out string? customerId) ||
        !Guid.TryParse(customerId, out _))
    {
        customerId = Guid.CreateVersion7().ToString("D");
        context.Response.Cookies.Append(
            ShopCustomer.CookieName,
            customerId,
            new CookieOptions
            {
                HttpOnly = true,
                SameSite = SameSiteMode.Strict,
                IsEssential = true,
                Path = "/",
            });
    }

    context.Items[ShopCustomer.ItemKey] = customerId;
    await next(context);
});

app.MapGet("/", (IOptions<ShopOptions> options) =>
    Results.Redirect($"/{options.Value.Storefronts.Keys.First()}/"));

app.MapGet("/{storefront}/", (string storefront, IOptions<ShopOptions> options) =>
    options.Value.Storefronts.ContainsKey(storefront)
        ? Results.Content(checkoutPage, "text/html; charset=utf-8")
        : Results.NotFound());

app.MapGet("/{storefront}/api/catalog", (string storefront, IOptions<ShopOptions> options) =>
    options.Value.Storefronts.TryGetValue(storefront, out StorefrontOptions? configuration)
        ? Results.Ok(new
        {
            name = configuration.DisplayName,
            currency = configuration.FiatCurrency,
            items = ShopCatalog.Items,
        })
        : Results.NotFound());

app.MapPost("/{storefront}/api/orders", async (
    string storefront,
    CreateOrderRequest request,
    HttpContext context,
    ShopPayments payments,
    CancellationToken cancellationToken) =>
{
    ShopItem? item = ShopCatalog.Find(request.Sku ?? string.Empty);
    if (item is null)
    {
        return Results.BadRequest(new { error = "unknown_item" });
    }

    return await payments.PlaceOrderAsync(
        storefront,
        ShopCustomer.Of(context),
        item,
        cancellationToken);
});

// Resumes an order whose Payment creation failed, with the same idempotency key, instead of
// placing a second order that could end up with a second Payment.
app.MapPost("/{storefront}/api/orders/{orderId:guid}/payment", async (
    string storefront,
    Guid orderId,
    HttpContext context,
    ShopPayments payments,
    CancellationToken cancellationToken) => await payments.ResumePaymentAsync(
        storefront,
        orderId,
        ShopCustomer.Of(context),
        cancellationToken));

app.MapGet("/{storefront}/api/orders/{orderId:guid}", (
    string storefront,
    Guid orderId,
    HttpContext context,
    OrderStore orders) =>
{
    ShopOrder? order = orders.FindForCustomer(storefront, orderId, ShopCustomer.Of(context));
    return order is null ? Results.NotFound() : Results.Ok(OrderView.Of(order));
});

app.MapPost("/{storefront}/api/orders/{orderId:guid}/currency", async (
    string storefront,
    Guid orderId,
    SelectCurrencyBody body,
    HttpContext context,
    ShopPayments payments,
    CancellationToken cancellationToken) => await payments.SelectCurrencyAsync(
        storefront,
        orderId,
        ShopCustomer.Of(context),
        body.Currency ?? string.Empty,
        cancellationToken));

// Test Mode only: stands in for the customer's wallet. Answers not found unless the storefront
// accepts test payments.
app.MapPost("/{storefront}/api/orders/{orderId:guid}/simulated-payment", async (
    string storefront,
    Guid orderId,
    SimulatePaymentBody body,
    HttpContext context,
    ShopPayments payments,
    CancellationToken cancellationToken) => await payments.SimulatePaymentAsync(
        storefront,
        orderId,
        ShopCustomer.Of(context),
        body.Amount,
        cancellationToken));

// The QR code is rendered here, by this backend, and served from this origin. The browser asks
// its own shop for it and never learns that Payaffe exists.
app.MapGet("/{storefront}/api/orders/{orderId:guid}/payment-code.svg", (
    string storefront,
    Guid orderId,
    HttpContext context,
    OrderStore orders) =>
{
    ShopOrder? order = orders.FindForCustomer(storefront, orderId, ShopCustomer.Of(context));
    if (order?.Instruction is null)
    {
        return Results.NotFound();
    }

    // Explicit colours, because the page loads this through <img>: an SVG document shown that
    // way inherits nothing from the page, so currentColor would be black on the dark theme's
    // near-black background, and a transparent code would lose its quiet zone there.
    PayaffePaymentQrCode code = PayaffePaymentQrCode.Create(
        order.Instruction,
        new PayaffeQrCodeOptions
        {
            DarkColor = "#000000",
            LightColor = "#ffffff",
            AccessibleLabel = $"Scan to pay order {order.OrderId:D}",
        });

    context.Response.Headers.CacheControl = "no-store";
    return Results.Content(code.ToSvg(), "image/svg+xml; charset=utf-8");
});

// The Webhook endpoint of one storefront. Its secret belongs to that storefront alone, so a
// Delivery meant for the other one does not verify here even though the route shape is the same.
app.MapPost("/{storefront}/webhooks/payaffe", async (
    string storefront,
    HttpContext context,
    ShopPayments payments,
    CancellationToken cancellationToken) =>
    await payments.HandleWebhookAsync(storefront, context, cancellationToken));

app.Run();

/// <summary>
/// Exposed so the sample's tests can start the application the way it actually runs.
/// </summary>
public partial class Program
{
    protected Program()
    {
    }
}
