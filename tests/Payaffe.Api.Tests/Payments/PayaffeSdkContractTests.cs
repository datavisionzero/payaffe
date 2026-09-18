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
}
