using Payaffe.Sdk;

namespace Payaffe.Api.Tests.Payments;

public sealed class PayaffeSdkContractTests
{
    [Fact]
    public async Task Sdk_completes_the_authenticated_headless_setup_flow_against_the_api()
    {
        const string token = "sdk-contract-token";
        await using PaymentApiFactory factory = new();
        await factory.SeedCredentialAsync(token);
        using HttpClient httpClient = factory.CreateClient();
        PayaffeClient client = new(httpClient, new Uri("http://localhost"), token);

        Payment created = await client.CreatePaymentAsync(
            new CreatePaymentRequest(
                "EUR",
                1999,
                "sdk-order-123",
                new PaymentContext(CustomerNumber: "C-1000"),
                "https://shop.example.test/orders/sdk-order-123"),
            "sdk-order-123");
        Payment read = await client.GetPaymentAsync(created.PaymentId);
        Payment selected = await client.SelectCurrencyAsync(
            created.PaymentId,
            SupportedCurrency.Btc);
        Payment replayedSelection = await client.SelectCurrencyAsync(
            created.PaymentId,
            SupportedCurrency.Btc);

        Assert.Equal(PaymentStatus.PendingCurrencySelection, created.Status);
        Assert.Equal(created.PaymentId, read.PaymentId);
        Assert.Equal(PaymentStatus.WaitingForPayment, selected.Status);
        Assert.Equal(SupportedCurrency.Btc, selected.SelectedCurrency);
        Assert.NotNull(selected.RateLock);
        Assert.NotNull(selected.PaymentInstruction);
        Assert.Equal(selected.PaymentInstruction, replayedSelection.PaymentInstruction);
        Assert.Equal("39980", selected.PaymentInstruction.AmountAtomic);
        Assert.StartsWith(
            "bitcoin:",
            selected.PaymentInstruction.Uri,
            StringComparison.Ordinal);
    }

    // The QR code a product shows must carry the instruction the API issued, for every currency
    // and at the precision the currency uses. The SDK tests decode the symbol; this one is here
    // because only a running API produces the URI that has to end up inside it.
    [Theory]
    [InlineData(
        "btc",
        "bitcoin:bc1qpayaffetestaddress0000000000000000000000000?amount=0.00039980")]
    [InlineData(
        "ltc",
        "litecoin:ltc1qpayaffetestaddress000000000000000000000000?amount=0.00039980")]
    [InlineData(
        "eth",
        "ethereum:0x1111111111111111111111111111111111111111@1?value=399800000000000")]
    public async Task Sdk_qr_code_carries_the_instruction_the_api_issued(
        string supportedCurrency,
        string expectedUri)
    {
        string token = $"sdk-qr-token-{supportedCurrency}";
        await using PaymentApiFactory factory = new();
        await factory.SeedCredentialAsync(token);
        using HttpClient httpClient = factory.CreateClient();
        PayaffeClient client = new(httpClient, new Uri("http://localhost"), token);

        Payment created = await client.CreatePaymentAsync(
            new CreatePaymentRequest("EUR", 1999, $"sdk-qr-{supportedCurrency}"),
            $"sdk-qr-{supportedCurrency}");
        Payment selected = await client.SelectCurrencyAsync(
            created.PaymentId,
            new SupportedCurrency(supportedCurrency.ToUpperInvariant()));

        Assert.NotNull(selected.PaymentInstruction);
        Assert.Equal(expectedUri, selected.PaymentInstruction.Uri);

        PayaffePaymentQrCode qrCode = PayaffePaymentQrCode.Create(selected.PaymentInstruction);

        Assert.Equal(selected.PaymentInstruction.Uri, qrCode.Payload);
        Assert.DoesNotContain("payaffe", qrCode.ToSvg(), StringComparison.OrdinalIgnoreCase);
    }
}
