using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Payaffe.Sdk;

namespace EmbeddedShop.Tests;

/// <summary>
/// What a customer does, and what the shop is allowed to conclude from it. The shop hands the
/// goods over only when Payaffe has said so through a channel the shop verified; a browser
/// saying "I paid" is not such a channel, and this suite has no endpoint that would accept one.
/// </summary>
public sealed class CheckoutTests
{
    [Fact]
    public async Task A_customer_pays_without_the_browser_ever_reaching_the_payment_provider()
    {
        using ShopApplication shop = new();
        using HttpClient browser = shop.CreateBrowser();

        JsonElement order = await PlaceOrderAsync(browser, ShopApplication.Teahouse);
        Assert.Equal("pending_currency_selection", order.GetProperty("paymentState").GetString());
        Assert.Contains(
            order.GetProperty("options").EnumerateArray(),
            option => option.GetProperty("currency").GetString() == "BTC" &&
                option.GetProperty("status").GetString() == "available");

        Guid orderId = order.GetProperty("orderId").GetGuid();
        JsonElement selected = await SelectAsync(browser, ShopApplication.Teahouse, orderId, "BTC");
        JsonElement instruction = selected.GetProperty("instruction");
        Assert.Equal("0.00039980", instruction.GetProperty("amount").GetString());
        Assert.Equal(FakePayaffe.PaymentAddress, instruction.GetProperty("address").GetString());
        Assert.Equal(FakePayaffe.PaymentUri, instruction.GetProperty("walletUri").GetString());

        // Nothing has been handed over yet, and the customer has seen an address.
        Assert.Equal("AwaitingPayment", selected.GetProperty("fulfillment").GetString());

        FakePayaffe.StoredPayment payment = shop.Payaffe.PaymentFor(orderId.ToString("D"));
        using HttpResponseMessage delivered = await browser.SendAsync(ShopApplication.Delivery(
            ShopApplication.Teahouse,
            ShopApplication.TeahouseWebhookSecret,
            Guid.CreateVersion7(),
            "payment.completed",
            payment.PaymentId,
            orderId,
            "completed"));
        Assert.Equal(HttpStatusCode.OK, delivered.StatusCode);

        JsonElement fulfilled = await ReadOrderAsync(browser, ShopApplication.Teahouse, orderId);
        Assert.Equal("Fulfilled", fulfilled.GetProperty("fulfillment").GetString());
        Assert.Equal("completed", fulfilled.GetProperty("paymentState").GetString());

        // Every call to Payaffe was made by the shop backend, with the storefront's own bearer
        // token. The browser made none of them: it only ever spoke to this shop.
        Assert.All(
            shop.Payaffe.Requests,
            request => Assert.Equal(FakePayaffe.TeahouseToken, request.Token));
        Assert.Contains(shop.Payaffe.Requests, request => request.Path == "/api/v1/payments");
    }

    /// <summary>
    /// A Test Mode installation signs its events like any other, so a production storefront that
    /// a test installation was pointed at would verify them. What stops the goods going out is the
    /// event saying it was simulated.
    /// </summary>
    [Fact]
    public async Task A_simulated_payment_does_not_hand_the_goods_over_in_a_production_storefront()
    {
        using ShopApplication shop = new();
        using HttpClient browser = shop.CreateBrowser();
        Guid orderId = (await PlaceOrderAsync(browser, ShopApplication.Teahouse)).GetProperty("orderId").GetGuid();
        await SelectAsync(browser, ShopApplication.Teahouse, orderId, "BTC");
        FakePayaffe.StoredPayment payment = shop.Payaffe.PaymentFor(orderId.ToString("D"));

        using HttpResponseMessage delivered = await browser.SendAsync(ShopApplication.Delivery(
            ShopApplication.Teahouse,
            ShopApplication.TeahouseWebhookSecret,
            Guid.CreateVersion7(),
            "payment.completed",
            payment.PaymentId,
            orderId,
            "completed",
            testMode: true));
        using HttpResponseMessage simulate = await browser.PostAsJsonAsync(
            $"/{ShopApplication.Teahouse}/api/orders/{orderId}/simulated-payment",
            new { amount = (string?)null });

        Assert.Equal(HttpStatusCode.OK, delivered.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, simulate.StatusCode);
        JsonElement order = await ReadOrderAsync(browser, ShopApplication.Teahouse, orderId);
        Assert.Equal("AwaitingPayment", order.GetProperty("fulfillment").GetString());
        Assert.True(order.GetProperty("testMode").GetBoolean());
        Assert.False(order.GetProperty("canSimulatePayment").GetBoolean());
    }

    [Fact]
    public async Task A_storefront_under_test_is_fulfilled_by_a_simulated_payment()
    {
        using ShopApplication shop = new() { TeahouseAcceptsTestPayments = true };
        using HttpClient browser = shop.CreateBrowser();
        Guid orderId = (await PlaceOrderAsync(browser, ShopApplication.Teahouse)).GetProperty("orderId").GetGuid();
        await SelectAsync(browser, ShopApplication.Teahouse, orderId, "BTC");
        FakePayaffe.StoredPayment payment = shop.Payaffe.PaymentFor(orderId.ToString("D"));

        using HttpResponseMessage delivered = await browser.SendAsync(ShopApplication.Delivery(
            ShopApplication.Teahouse,
            ShopApplication.TeahouseWebhookSecret,
            Guid.CreateVersion7(),
            "payment.completed",
            payment.PaymentId,
            orderId,
            "completed",
            testMode: true));

        Assert.Equal(HttpStatusCode.OK, delivered.StatusCode);
        JsonElement order = await ReadOrderAsync(browser, ShopApplication.Teahouse, orderId);
        Assert.Equal("Fulfilled", order.GetProperty("fulfillment").GetString());
    }

    [Fact]
    public async Task A_repeated_delivery_does_not_hand_the_goods_over_twice()
    {
        using ShopApplication shop = new();
        using HttpClient browser = shop.CreateBrowser();
        (Guid orderId, Guid eventId, Guid paymentId) = await PaidOrderAsync(shop, browser);

        // The same event again, as an at-least-once channel will eventually do.
        for (int attempt = 0; attempt < 3; attempt++)
        {
            using HttpResponseMessage repeat = await browser.SendAsync(ShopApplication.Delivery(
                ShopApplication.Teahouse,
                ShopApplication.TeahouseWebhookSecret,
                eventId,
                "payment.completed",
                paymentId,
                orderId,
                "completed"));
            Assert.Equal(HttpStatusCode.OK, repeat.StatusCode);
            Assert.Equal("duplicate", (await repeat.Content.ReadFromJsonAsync<JsonElement>())
                .GetProperty("status").GetString());
        }

        Assert.Equal(1, FulfillmentCount(shop, orderId));
    }

    [Fact]
    public async Task A_delivery_that_does_not_verify_changes_nothing()
    {
        using ShopApplication shop = new();
        using HttpClient browser = shop.CreateBrowser();
        Guid orderId = await SelectedOrderAsync(browser, ShopApplication.Teahouse);
        Guid paymentId = shop.Payaffe.PaymentFor(orderId.ToString("D")).PaymentId;

        // Signed with the other storefront's secret, which is exactly what an attacker who read
        // one storefront's secret would have.
        using HttpResponseMessage wrongSecret = await browser.SendAsync(ShopApplication.Delivery(
            ShopApplication.Teahouse,
            ShopApplication.RoasteryWebhookSecret,
            Guid.CreateVersion7(),
            "payment.completed",
            paymentId,
            orderId,
            "completed"));

        // Correctly signed, but hours old: outside the replay window.
        using HttpResponseMessage stale = await browser.SendAsync(ShopApplication.Delivery(
            ShopApplication.Teahouse,
            ShopApplication.TeahouseWebhookSecret,
            Guid.CreateVersion7(),
            "payment.completed",
            paymentId,
            orderId,
            "completed",
            DateTimeOffset.UtcNow.AddHours(-3)));

        Assert.Equal(HttpStatusCode.Unauthorized, wrongSecret.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, stale.StatusCode);

        JsonElement order = await ReadOrderAsync(browser, ShopApplication.Teahouse, orderId);
        Assert.Equal("AwaitingPayment", order.GetProperty("fulfillment").GetString());
    }

    [Fact]
    public async Task The_payment_code_comes_from_the_shop_and_carries_the_instruction_uri()
    {
        using ShopApplication shop = new();
        using HttpClient browser = shop.CreateBrowser();
        Guid orderId = await SelectedOrderAsync(browser, ShopApplication.Teahouse);

        JsonElement order = await ReadOrderAsync(browser, ShopApplication.Teahouse, orderId);
        string qrUrl = order.GetProperty("instruction").GetProperty("qrCodeUrl").GetString()!;
        Assert.StartsWith($"/{ShopApplication.Teahouse}/api/orders/", qrUrl, StringComparison.Ordinal);

        using HttpResponseMessage response = await browser.GetAsync(qrUrl);
        string svg = await response.Content.ReadAsStringAsync();

        Assert.Equal("image/svg+xml", response.Content.Headers.ContentType?.MediaType);
        Assert.StartsWith("<svg", svg, StringComparison.Ordinal);
        Assert.DoesNotContain("payaffe", svg, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("http", svg.Replace("http://www.w3.org/2000/svg", "", StringComparison.Ordinal), StringComparison.OrdinalIgnoreCase);

        // The code is the instruction, not something the shop derived from the amount.
        Assert.Equal(
            FakePayaffe.PaymentUri,
            PayaffePaymentQrCode.CreateForUri(FakePayaffe.PaymentUri).Payload);
    }

    [Fact]
    public async Task Nothing_the_browser_receives_points_at_the_payment_provider()
    {
        using ShopApplication shop = new();
        using HttpClient browser = shop.CreateBrowser();
        Guid orderId = await SelectedOrderAsync(browser, ShopApplication.Teahouse);

        string[] paths =
        [
            $"/{ShopApplication.Teahouse}/",
            "/assets/checkout.js",
            "/assets/styles.css",
            $"/{ShopApplication.Teahouse}/api/orders/{orderId:D}",
        ];

        List<string> served = [];
        foreach (string path in paths)
        {
            using HttpResponseMessage response = await browser.GetAsync(path);
            Assert.True(response.IsSuccessStatusCode, $"{path} returned {response.StatusCode}.");
            served.Add(await response.Content.ReadAsStringAsync());
        }

        foreach (string document in served)
        {
            Assert.DoesNotContain("payaffe", document, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("payerPageUrl", document, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Bearer", document, StringComparison.Ordinal);
            Assert.DoesNotContain("://", document, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task An_order_belongs_to_one_customer_of_one_storefront()
    {
        using ShopApplication shop = new();
        using HttpClient browser = shop.CreateBrowser();
        using HttpClient stranger = shop.CreateBrowser();
        Guid orderId = await SelectedOrderAsync(browser, ShopApplication.Teahouse);

        // Another browser, therefore another customer of the shop.
        using HttpResponseMessage byStranger = await stranger.GetAsync(
            $"/{ShopApplication.Teahouse}/api/orders/{orderId:D}");

        // The same customer, but the other storefront. The order is not theirs to see either.
        using HttpResponseMessage byOtherStorefront = await browser.GetAsync(
            $"/{ShopApplication.Roastery}/api/orders/{orderId:D}");

        using HttpResponseMessage strangerSelects = await stranger.PostAsJsonAsync(
            $"/{ShopApplication.Teahouse}/api/orders/{orderId:D}/currency",
            new { currency = "LTC" });

        Assert.Equal(HttpStatusCode.NotFound, byStranger.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, byOtherStorefront.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, strangerSelects.StatusCode);
    }

    [Fact]
    public async Task A_delivery_to_the_wrong_storefront_does_not_fulfil_the_order()
    {
        using ShopApplication shop = new();
        using HttpClient browser = shop.CreateBrowser();
        Guid orderId = await SelectedOrderAsync(browser, ShopApplication.Teahouse);
        Guid paymentId = shop.Payaffe.PaymentFor(orderId.ToString("D")).PaymentId;

        // Correctly signed for the roastery, naming a teahouse order. It verifies, and it still
        // fulfils nothing, because the roastery has no such order.
        using HttpResponseMessage response = await browser.SendAsync(ShopApplication.Delivery(
            ShopApplication.Roastery,
            ShopApplication.RoasteryWebhookSecret,
            Guid.CreateVersion7(),
            "payment.completed",
            paymentId,
            orderId,
            "completed"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            "ignored",
            (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("status").GetString());

        JsonElement order = await ReadOrderAsync(browser, ShopApplication.Teahouse, orderId);
        Assert.Equal("AwaitingPayment", order.GetProperty("fulfillment").GetString());
    }

    [Fact]
    public async Task Polling_completes_the_order_when_no_delivery_ever_arrives()
    {
        using ShopApplication shop = new() { ReconcileByPolling = true };
        using HttpClient browser = shop.CreateBrowser();
        Guid orderId = await SelectedOrderAsync(browser, ShopApplication.Teahouse);

        // The transfer arrives and the Delivery does not. The reconciler's read is the only
        // thing that will notice.
        shop.Payaffe.CompletedPayments.Add(shop.Payaffe.PaymentFor(orderId.ToString("D")).PaymentId);

        JsonElement order = await WaitForAsync(
            browser,
            ShopApplication.Teahouse,
            orderId,
            view => view.GetProperty("fulfillment").GetString() == "Fulfilled");

        Assert.Equal("polling", order.GetProperty("lastSignal").GetString());
        Assert.Equal(1, FulfillmentCount(shop, orderId));
    }

    [Fact]
    public async Task A_losing_selection_shows_the_winning_instruction_rather_than_replacing_it()
    {
        using ShopApplication shop = new();
        using HttpClient browser = shop.CreateBrowser();
        Guid orderId = await PlaceOrderIdAsync(browser, ShopApplication.Teahouse);

        // The first tab committed BTC; this one asked for LTC and lost.
        await SelectAsync(browser, ShopApplication.Teahouse, orderId, "BTC");
        shop.Payaffe.NextSelectionFailure = "payment.currency_already_selected";
        JsonElement loser = await SelectAsync(browser, ShopApplication.Teahouse, orderId, "LTC");

        Assert.Equal("BTC", loser.GetProperty("selectedCurrency").GetString());
        Assert.Equal(
            FakePayaffe.PaymentUri,
            loser.GetProperty("instruction").GetProperty("walletUri").GetString());
    }

    [Fact]
    public async Task An_unavailable_rate_leaves_the_order_where_it_was()
    {
        using ShopApplication shop = new();
        using HttpClient browser = shop.CreateBrowser();
        Guid orderId = await PlaceOrderIdAsync(browser, ShopApplication.Teahouse);
        shop.Payaffe.NextSelectionFailure = "exchange_rate.unavailable";

        using HttpResponseMessage refused = await browser.PostAsJsonAsync(
            $"/{ShopApplication.Teahouse}/api/orders/{orderId:D}/currency",
            new { currency = "BTC" });

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        JsonElement problem = await refused.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("rate_unavailable", problem.GetProperty("error").GetString());
        Assert.True(problem.GetProperty("retryable").GetBoolean());

        // No address was handed out, and the same currency can be chosen again.
        JsonElement order = await ReadOrderAsync(browser, ShopApplication.Teahouse, orderId);
        Assert.Equal("pending_currency_selection", order.GetProperty("paymentState").GetString());
        Assert.Equal(JsonValueKind.Null, order.GetProperty("instruction").ValueKind);

        JsonElement retried = await SelectAsync(browser, ShopApplication.Teahouse, orderId, "BTC");
        Assert.Equal("BTC", retried.GetProperty("selectedCurrency").GetString());
    }

    [Fact]
    public async Task An_expired_checkout_stays_open_for_a_late_transfer()
    {
        using ShopApplication shop = new();
        using HttpClient browser = shop.CreateBrowser();
        Guid orderId = await SelectedOrderAsync(browser, ShopApplication.Teahouse);
        Guid paymentId = shop.Payaffe.PaymentFor(orderId.ToString("D")).PaymentId;

        using HttpResponseMessage expiry = await browser.SendAsync(ShopApplication.Delivery(
            ShopApplication.Teahouse,
            ShopApplication.TeahouseWebhookSecret,
            Guid.CreateVersion7(),
            "payment.expired",
            paymentId,
            orderId,
            "expired"));
        Assert.Equal(HttpStatusCode.OK, expiry.StatusCode);

        JsonElement expired = await ReadOrderAsync(browser, ShopApplication.Teahouse, orderId);
        Assert.Equal("Expired", expired.GetProperty("fulfillment").GetString());

        // The transfer was on its way. Expiry was not the end of the order.
        using HttpResponseMessage late = await browser.SendAsync(ShopApplication.Delivery(
            ShopApplication.Teahouse,
            ShopApplication.TeahouseWebhookSecret,
            Guid.CreateVersion7(),
            "payment.settled",
            paymentId,
            orderId,
            "settled"));
        Assert.Equal(HttpStatusCode.OK, late.StatusCode);

        JsonElement settled = await ReadOrderAsync(browser, ShopApplication.Teahouse, orderId);
        Assert.Equal("Fulfilled", settled.GetProperty("fulfillment").GetString());
        Assert.Equal(1, FulfillmentCount(shop, orderId));
    }

    private static async Task<JsonElement> PlaceOrderAsync(HttpClient browser, string storefront)
    {
        using HttpResponseMessage response = await browser.PostAsJsonAsync(
            $"/{storefront}/api/orders",
            new { sku = "mug-01" });
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<Guid> PlaceOrderIdAsync(HttpClient browser, string storefront) =>
        (await PlaceOrderAsync(browser, storefront)).GetProperty("orderId").GetGuid();

    private static async Task<JsonElement> SelectAsync(
        HttpClient browser,
        string storefront,
        Guid orderId,
        string currency)
    {
        using HttpResponseMessage response = await browser.PostAsJsonAsync(
            $"/{storefront}/api/orders/{orderId:D}/currency",
            new { currency });
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<Guid> SelectedOrderAsync(HttpClient browser, string storefront)
    {
        Guid orderId = await PlaceOrderIdAsync(browser, storefront);
        await SelectAsync(browser, storefront, orderId, "BTC");
        return orderId;
    }

    private static async Task<JsonElement> ReadOrderAsync(
        HttpClient browser,
        string storefront,
        Guid orderId)
    {
        using HttpResponseMessage response = await browser.GetAsync(
            $"/{storefront}/api/orders/{orderId:D}");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static async Task<JsonElement> WaitForAsync(
        HttpClient browser,
        string storefront,
        Guid orderId,
        Func<JsonElement, bool> condition)
    {
        for (int attempt = 0; attempt < 100; attempt++)
        {
            JsonElement order = await ReadOrderAsync(browser, storefront, orderId);
            if (condition(order))
            {
                return order;
            }

            await Task.Delay(100);
        }

        throw new TimeoutException("The order never reached the expected state.");
    }

    private static async Task<(Guid OrderId, Guid EventId, Guid PaymentId)> PaidOrderAsync(
        ShopApplication shop,
        HttpClient browser)
    {
        Guid orderId = await SelectedOrderAsync(browser, ShopApplication.Teahouse);
        Guid paymentId = shop.Payaffe.PaymentFor(orderId.ToString("D")).PaymentId;
        Guid eventId = Guid.CreateVersion7();

        using HttpResponseMessage response = await browser.SendAsync(ShopApplication.Delivery(
            ShopApplication.Teahouse,
            ShopApplication.TeahouseWebhookSecret,
            eventId,
            "payment.completed",
            paymentId,
            orderId,
            "completed"));
        response.EnsureSuccessStatusCode();
        return (orderId, eventId, paymentId);
    }

    private static int FulfillmentCount(ShopApplication shop, Guid orderId)
    {
        OrderStore orders = shop.Services.GetRequiredService<OrderStore>();
        return orders.FindByExternalReference(ShopApplication.Teahouse, orderId.ToString("D"))!
            .FulfillmentCount;
    }
}
