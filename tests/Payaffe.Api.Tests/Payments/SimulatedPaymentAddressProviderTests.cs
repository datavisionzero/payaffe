using Payaffe.Application;
using Payaffe.Application.Installation;
using Payaffe.Application.Payments;
using Payaffe.Infrastructure;
using Payaffe.Infrastructure.Payments;
using Payaffe.Infrastructure.Persistence;
using Payaffe.Infrastructure.Persistence.Records;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NBitcoin;
using NBitcoin.Altcoins;

namespace Payaffe.Api.Tests.Payments;

public sealed class SimulatedPaymentAddressProviderTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-09-23T12:00:00Z");

    [Theory]
    [InlineData("BTC")]
    [InlineData("LTC")]
    [InlineData("ETH")]
    public async Task Every_currency_is_available_in_a_project_without_a_wallet_source(string currency)
    {
        await using var dbContext = CreateDbContext();
        var provider = new SimulatedPaymentAddressProvider(dbContext, new FixedClock());

        Assert.True(await provider.IsAddressAvailableAsync(Guid.NewGuid(), currency, CancellationToken.None));
    }

    [Theory]
    [InlineData("BTC")]
    [InlineData("LTC")]
    [InlineData("ETH")]
    public async Task Each_payment_gets_its_own_address_and_keeps_it(string currency)
    {
        await using var dbContext = CreateDbContext();
        var firstPaymentId = Guid.NewGuid();
        var secondPaymentId = Guid.NewGuid();
        SeedPayment(dbContext, firstPaymentId);
        SeedPayment(dbContext, secondPaymentId);
        await dbContext.SaveChangesAsync();
        var provider = new SimulatedPaymentAddressProvider(dbContext, new FixedClock());

        var first = await provider.AssignAsync(ProjectDefaults.DefaultProjectId, firstPaymentId, currency, CancellationToken.None);
        var repeated = await provider.AssignAsync(ProjectDefaults.DefaultProjectId, firstPaymentId, currency, CancellationToken.None);
        var otherCurrency = await provider.AssignAsync(
            ProjectDefaults.DefaultProjectId,
            firstPaymentId,
            currency == "BTC" ? "LTC" : "BTC",
            CancellationToken.None);
        var second = await provider.AssignAsync(ProjectDefaults.DefaultProjectId, secondPaymentId, currency, CancellationToken.None);

        Assert.NotNull(first);
        Assert.Equal(first, repeated);
        Assert.Null(otherCurrency);
        Assert.NotEqual(first.PaymentAddress, second!.PaymentAddress);
        Assert.Equal("testnet", first.Network);
        Assert.Equal(currency == "ETH" ? SimulatedPaymentAddressProvider.NativeEthChainId : null, first.ChainId);
    }

    /// <summary>
    /// The one property that matters most: a wallet on mainnet cannot be
    /// talked into paying a simulated address.
    /// </summary>
    [Fact]
    public void Simulated_addresses_are_not_mainnet_addresses()
    {
        var paymentId = Guid.NewGuid();
        var btc = SimulatedPaymentAddressProvider.CreateAddress(paymentId, "BTC")!;
        var ltc = SimulatedPaymentAddressProvider.CreateAddress(paymentId, "LTC")!;

        Assert.ThrowsAny<FormatException>(() => BitcoinAddress.Create(btc, Network.Main));
        Assert.ThrowsAny<FormatException>(() => BitcoinAddress.Create(ltc, Litecoin.Instance.Mainnet));
        Assert.NotNull(BitcoinAddress.Create(btc, Network.TestNet));
        Assert.NotNull(BitcoinAddress.Create(ltc, Litecoin.Instance.Testnet));
        Assert.NotEqual(1, SimulatedPaymentAddressProvider.NativeEthChainId);
    }

    [Theory]
    [InlineData("BTC", "bitcoin:?tb=")]
    [InlineData("LTC", "litecoin:tltc1")]
    [InlineData("ETH", "ethereum:0x")]
    public void Simulated_addresses_produce_testnet_wallet_uris(string currency, string expectedPrefix)
    {
        var address = SimulatedPaymentAddressProvider.CreateAddress(Guid.NewGuid(), currency)!;
        var uri = PaymentInstructionFactory.BuildUri(new PaymentInstructionReadModel(
            currency,
            SimulatedPaymentAddressProvider.Network,
            currency == "ETH" ? SimulatedPaymentAddressProvider.NativeEthChainId : null,
            "0.001",
            address));

        Assert.StartsWith(expectedPrefix, uri, StringComparison.Ordinal);
        if (currency == "ETH")
        {
            Assert.Contains("@11155111", uri, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("test", typeof(SimulatedPaymentAddressProvider))]
    [InlineData("live", typeof(EfPaymentAddressProvider))]
    [InlineData(null, typeof(EfPaymentAddressProvider))]
    public void The_installation_mode_decides_the_address_source(string? mode, Type expected)
    {
        var services = new ServiceCollection();
        services.AddSingleton(ConfiguredInstallationMode.FromConfiguration(mode));
        services.AddPayaffeApplication();
        services.AddPayaffeInfrastructure("Host=unused");
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        Assert.IsType(expected, scope.ServiceProvider.GetRequiredService<IPaymentAddressProvider>());
    }

    private static PayaffeDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<PayaffeDbContext>()
            .UseInMemoryDatabase($"simulated-address-provider-{Guid.NewGuid()}")
            .Options);

    private static void SeedPayment(PayaffeDbContext dbContext, Guid paymentId)
    {
        dbContext.Payments.Add(new PaymentRecord
        {
            Id = paymentId,
            IntegrationApiCredentialId = Guid.NewGuid(),
            ExternalReference = $"payment-{paymentId:D}",
            FiatCurrency = "EUR",
            FiatAmountMinor = 1000,
            Status = "pending_currency_selection",
            PayerPageId = $"payer-{paymentId:N}",
            ExpiresAt = Now.AddHours(1),
            LateAcceptanceEndsAt = Now.AddDays(1),
            CreatedAt = Now,
            UpdatedAt = Now,
        });
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => Now;
    }
}
