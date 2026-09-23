using Payaffe.Sdk;

namespace Payaffe.Api.Tests.Payments;

/// <summary>
/// What an integration sees of the Installation Mode through the Integration
/// API (ADR 0033).
/// </summary>
public sealed class TestModeApiTests
{
    private const string Token = "test-mode-token";

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Every_payment_response_says_whether_the_installation_is_in_test_mode(bool testMode)
    {
        await using PaymentApiFactory factory = new() { TestMode = testMode };
        await factory.SeedCredentialAsync(Token);
        using HttpClient httpClient = factory.CreateClient();
        PayaffeClient payaffe = new(httpClient, new Uri("http://localhost"), Token);

        Payment created = await payaffe.CreatePaymentAsync(
            new CreatePaymentRequest("EUR", 1999, "test-mode-order"),
            idempotencyKey: "test-mode-order-1");
        Payment selected = await payaffe.SelectCurrencyAsync(created.PaymentId, SupportedCurrency.Btc);
        Payment read = await payaffe.GetPaymentAsync(created.PaymentId);

        Assert.All([created, selected, read], payment => Assert.Equal(testMode, payment.TestMode));
    }

    /// <summary>
    /// No wallet source, no provider and no rate source is configured, and
    /// every currency can still be paid.
    /// </summary>
    [Fact]
    public async Task A_test_installation_offers_every_currency_with_nothing_configured()
    {
        await using PaymentApiFactory factory = new() { TestMode = true };
        await factory.SeedCredentialAsync(Token);
        using HttpClient httpClient = factory.CreateClient();
        PayaffeClient payaffe = new(httpClient, new Uri("http://localhost"), Token);

        Payment created = await payaffe.CreatePaymentAsync(
            new CreatePaymentRequest("EUR", 2500, "test-mode-eth"),
            idempotencyKey: "test-mode-eth-1");
        Payment selected = await payaffe.SelectCurrencyAsync(created.PaymentId, SupportedCurrency.Eth);

        Assert.All(created.PaymentOptions, option => Assert.Equal(PaymentOptionStatus.Available, option.Status));
        PaymentInstruction instruction = Assert.IsType<PaymentInstruction>(selected.PaymentInstruction);
        Assert.Equal("testnet", instruction.Network);
        Assert.Equal(11_155_111, instruction.ChainId);
        Assert.Equal("0.01", instruction.Amount);
        Assert.Equal("simulated", selected.RateLock!.Source);
    }
}
