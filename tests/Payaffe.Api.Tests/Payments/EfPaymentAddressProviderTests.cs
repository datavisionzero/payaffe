using Payaffe.Application.Payments;
using Payaffe.Infrastructure.Payments;
using Payaffe.Infrastructure.Persistence;
using Payaffe.Infrastructure.Persistence.Records;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NBitcoin;
using NBitcoin.Altcoins;

namespace Payaffe.Api.Tests.Payments;

public sealed class EfPaymentAddressProviderTests
{
    private static readonly DateTimeOffset Now =
        DateTimeOffset.Parse("2026-07-25T12:00:00Z");

    [Fact]
    public async Task Watch_only_source_derives_unique_restart_safe_addresses()
    {
        var options = CreateDbOptions();
        var firstPaymentId = Guid.NewGuid();
        var secondPaymentId = Guid.NewGuid();
        var extendedPublicKey = new ExtKey()
            .Neuter()
            .ToString(Network.TestNet);
        await using (var dbContext = new PayaffeDbContext(options))
        {
            SeedPayment(dbContext, firstPaymentId);
            SeedPayment(dbContext, secondPaymentId);
            await dbContext.SaveChangesAsync();
            var provider = CreateProvider(
                dbContext,
                new PaymentAddressOptions
                {
                    Btc = new WatchOnlyWalletSourceOptions
                    {
                        Enabled = true,
                        ExtendedPublicKey = extendedPublicKey,
                        Network = "testnet",
                        AddressType = "segwit",
                    },
                });

            var first = await provider.AssignAsync(
                ProjectDefaults.DefaultProjectId,
                firstPaymentId,
                "BTC",
                CancellationToken.None);
            var repeated = await provider.AssignAsync(
                ProjectDefaults.DefaultProjectId,
                firstPaymentId,
                "BTC",
                CancellationToken.None);
            var second = await provider.AssignAsync(
                ProjectDefaults.DefaultProjectId,
                secondPaymentId,
                "BTC",
                CancellationToken.None);

            Assert.NotNull(first);
            Assert.Equal(first, repeated);
            Assert.NotEqual(first.PaymentAddress, second!.PaymentAddress);
            Assert.StartsWith("tb1", first.PaymentAddress, StringComparison.Ordinal);
        }

        await using var restartedDbContext = new PayaffeDbContext(options);
        var cursor = Assert.Single(restartedDbContext.WatchOnlyWalletCursors);
        Assert.Equal(2, cursor.NextDerivationIndex);
        Assert.DoesNotContain(extendedPublicKey, cursor.SourceFingerprint, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Watch_only_source_assigns_the_receive_addresses_of_the_configured_account()
    {
        // The operator configures the account node their wallet exports. What
        // payaffe hands a payer has to be the address that wallet shows at
        // receive index 0, or the money lands somewhere its owner cannot see.
        // A fixed seed rather than a random key, because the whole point of
        // this test is the path, and a key at depth 0 has none.
        var seed = new byte[64];
        for (var i = 0; i < seed.Length; i++)
        {
            seed[i] = (byte)i;
        }

        var master = ExtKey.CreateFromSeed(seed);
        var account = master.Derive(new KeyPath("84'/0'/0'"));
        var expectedFirst = master
            .Derive(new KeyPath("84'/0'/0'/0/0"))
            .Neuter()
            .PubKey
            .GetAddress(ScriptPubKeyType.Segwit, Network.Main)
            .ToString();
        var expectedSecond = master
            .Derive(new KeyPath("84'/0'/0'/0/1"))
            .Neuter()
            .PubKey
            .GetAddress(ScriptPubKeyType.Segwit, Network.Main)
            .ToString();

        var firstPaymentId = Guid.NewGuid();
        var secondPaymentId = Guid.NewGuid();
        await using var dbContext = new PayaffeDbContext(CreateDbOptions());
        SeedPayment(dbContext, firstPaymentId);
        SeedPayment(dbContext, secondPaymentId);
        await dbContext.SaveChangesAsync();
        var provider = CreateProvider(
            dbContext,
            new PaymentAddressOptions
            {
                Btc = new WatchOnlyWalletSourceOptions
                {
                    Enabled = true,
                    ExtendedPublicKey = account.Neuter().ToString(Network.Main),
                    Network = "mainnet",
                    AddressType = "segwit",
                },
            });

        var first = await provider.AssignAsync(
            ProjectDefaults.DefaultProjectId,
            firstPaymentId,
            "BTC",
            CancellationToken.None);
        var second = await provider.AssignAsync(
            ProjectDefaults.DefaultProjectId,
            secondPaymentId,
            "BTC",
            CancellationToken.None);

        Assert.Equal(expectedFirst, first!.PaymentAddress);
        Assert.Equal(expectedSecond, second!.PaymentAddress);

        // The account node itself, derived directly, is what this used to hand
        // out. Naming it here keeps the regression from coming back quietly.
        var changeLevelAddress = account
            .Neuter()
            .Derive(0u)
            .PubKey
            .GetAddress(ScriptPubKeyType.Segwit, Network.Main)
            .ToString();
        Assert.NotEqual(changeLevelAddress, first.PaymentAddress);
    }

    [Fact]
    public async Task Native_eth_pool_assigns_each_address_only_once()
    {
        var options = CreateDbOptions();
        var firstPaymentId = Guid.NewGuid();
        var secondPaymentId = Guid.NewGuid();
        await using var dbContext = new PayaffeDbContext(options);
        SeedPayment(dbContext, firstPaymentId);
        SeedPayment(dbContext, secondPaymentId);
        var importId = Guid.NewGuid();
        dbContext.NativeEthAddressPoolImports.Add(new NativeEthAddressPoolImportRecord
        {
            Id = importId,
            ImportedByAdminAccountId = Guid.NewGuid(),
            AddressCount = 1,
            ImportedAt = Now,
        });
        dbContext.NativeEthAddresses.Add(new NativeEthAddressRecord
        {
            Id = Guid.NewGuid(),
            ImportId = importId,
            Address = "0x1111111111111111111111111111111111111111",
            Status = "unused",
            CreatedAt = Now,
            UpdatedAt = Now,
            Version = 1,
        });
        await dbContext.SaveChangesAsync();
        var provider = CreateProvider(dbContext, new PaymentAddressOptions());

        Assert.True(await provider.IsAddressAvailableAsync(ProjectDefaults.DefaultProjectId, "ETH", CancellationToken.None));
        var first = await provider.AssignAsync(ProjectDefaults.DefaultProjectId, firstPaymentId, "ETH", CancellationToken.None);
        var repeated = await provider.AssignAsync(ProjectDefaults.DefaultProjectId, firstPaymentId, "ETH", CancellationToken.None);
        var exhausted = await provider.AssignAsync(ProjectDefaults.DefaultProjectId, secondPaymentId, "ETH", CancellationToken.None);

        Assert.Equal("0x1111111111111111111111111111111111111111", first!.PaymentAddress);
        Assert.Equal(first, repeated);
        Assert.Null(exhausted);
        Assert.False(await provider.IsAddressAvailableAsync(ProjectDefaults.DefaultProjectId, "ETH", CancellationToken.None));
        var poolAddress = Assert.Single(dbContext.NativeEthAddresses);
        Assert.Equal("assigned", poolAddress.Status);
        Assert.Equal(firstPaymentId, poolAddress.AssignedPaymentId);
    }

    [Fact]
    public async Task Litecoin_watch_only_source_uses_litecoin_network_encoding()
    {
        var paymentId = Guid.NewGuid();
        await using var dbContext = new PayaffeDbContext(CreateDbOptions());
        SeedPayment(dbContext, paymentId);
        await dbContext.SaveChangesAsync();
        var provider = CreateProvider(
            dbContext,
            new PaymentAddressOptions
            {
                Ltc = new WatchOnlyWalletSourceOptions
                {
                    Enabled = true,
                    ExtendedPublicKey = new ExtKey()
                        .Neuter()
                        .ToString(Litecoin.Instance.Testnet),
                    Network = "testnet",
                    AddressType = "segwit",
                },
            });

        var assignment = await provider.AssignAsync(
            ProjectDefaults.DefaultProjectId,
            paymentId,
            "LTC",
            CancellationToken.None);

        Assert.NotNull(assignment);
        Assert.StartsWith("tltc1", assignment.PaymentAddress, StringComparison.Ordinal);
    }

    private static DbContextOptions<PayaffeDbContext> CreateDbOptions() =>
        new DbContextOptionsBuilder<PayaffeDbContext>()
            .UseInMemoryDatabase($"payment-address-provider-{Guid.NewGuid()}")
            .Options;

    private static EfPaymentAddressProvider CreateProvider(
        PayaffeDbContext dbContext,
        PaymentAddressOptions options) =>
        new(dbContext, new FixedClock(), Options.Create(options));

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
            Version = 1,
        });
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => Now;
    }
}
