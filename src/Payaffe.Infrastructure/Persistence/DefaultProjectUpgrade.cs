using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Payaffe.Application.Payments;
using Payaffe.Infrastructure.Payments;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Payaffe.Infrastructure.Persistence;

internal static class DefaultProjectUpgrade
{
    public static async Task ApplyAsync(
        IServiceProvider serviceProvider,
        CancellationToken cancellationToken)
    {
        var dbContext = serviceProvider.GetRequiredService<PayaffeDbContext>();
        var paymentOptions = serviceProvider.GetRequiredService<IOptions<PaymentApplicationOptions>>().Value;
        var addressOptions = serviceProvider.GetRequiredService<IOptions<PaymentAddressOptions>>().Value;
        var fingerprint = ComputeFingerprint(paymentOptions, addressOptions);

        var configuration = await dbContext.ProjectConfigurations
            .SingleAsync(
                candidate => candidate.ProjectId == ProjectDefaults.DefaultProjectId,
                cancellationToken);

        if (configuration.LegacySettingsFingerprint.Length > 0)
        {
            if (!CryptographicOperations.FixedTimeEquals(
                    Convert.FromHexString(configuration.LegacySettingsFingerprint),
                    Convert.FromHexString(fingerprint)))
            {
                throw new InvalidOperationException(
                    "The effective legacy Project settings differ from the values that created the default Project. " +
                    "All starting hosts must use the same Payments and PaymentAddresses settings during the multi-Project upgrade.");
            }

        }

        if (configuration.LegacySettingsFingerprint.Length == 0)
        {
            configuration.PaymentExpirationSeconds = ToWholeSeconds(
                paymentOptions.PaymentExpiration,
                "Payments:PaymentExpiration");
            configuration.LateAcceptanceWindowSeconds = ToWholeSeconds(
                paymentOptions.LateAcceptanceWindow,
                "Payments:LateAcceptanceWindow",
                allowZero: true);
            configuration.PaymentTolerancePercent = paymentOptions.PaymentTolerancePercent;
            configuration.BtcConfirmationRequirement = paymentOptions.BtcConfirmationRequirement;
            configuration.LtcConfirmationRequirement = paymentOptions.LtcConfirmationRequirement;
            configuration.EthConfirmationRequirement = paymentOptions.EthConfirmationRequirement;
            configuration.BtcReorgMonitoringDepth = paymentOptions.BtcReorgMonitoringDepth;
            configuration.LtcReorgMonitoringDepth = paymentOptions.LtcReorgMonitoringDepth;
            configuration.EthReorgMonitoringDepth = paymentOptions.EthReorgMonitoringDepth;
            configuration.NativeEthLowCapacityThreshold = addressOptions.NativeEthLowCapacityThreshold;
            configuration.LegacySettingsFingerprint = fingerprint;
            configuration.UpdatedAt = DateTimeOffset.UtcNow;
            configuration.Version++;
        }

        await MaterializeWalletSourceAsync("BTC", addressOptions.Btc);
        await MaterializeWalletSourceAsync("LTC", addressOptions.Ltc);

        var selectedPayments = await dbContext.Payments
            .Where(payment => payment.ProjectId == ProjectDefaults.DefaultProjectId &&
                              payment.SelectedCurrency != null &&
                              payment.ConfirmationRequirement == null)
            .ToListAsync(cancellationToken);
        foreach (var payment in selectedPayments)
        {
            payment.ConfirmationRequirement = payment.SelectedCurrency switch
            {
                "BTC" => configuration.BtcConfirmationRequirement,
                "LTC" => configuration.LtcConfirmationRequirement,
                "ETH" => configuration.EthConfirmationRequirement,
                _ => null,
            };
            payment.PaymentTolerancePercent = configuration.PaymentTolerancePercent;
            payment.ReorgMonitoringDepth = payment.SelectedCurrency switch
            {
                "BTC" => configuration.BtcReorgMonitoringDepth,
                "LTC" => configuration.LtcReorgMonitoringDepth,
                "ETH" => configuration.EthReorgMonitoringDepth,
                _ => null,
            };
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        async Task MaterializeWalletSourceAsync(
            string supportedCurrency,
            WatchOnlyWalletSourceOptions source)
        {
            if (!source.Enabled || string.IsNullOrWhiteSpace(source.ExtendedPublicKey))
            {
                return;
            }

            var sourceFingerprint = WatchOnlyWalletSourceFingerprint.Compute(
                supportedCurrency,
                source.Network,
                source.AddressType,
                source.ExtendedPublicKey);
            var existing = await dbContext.ProjectWatchOnlyWalletSources.SingleOrDefaultAsync(
                candidate => candidate.ProjectId == ProjectDefaults.DefaultProjectId &&
                             candidate.SupportedCurrency == supportedCurrency,
                cancellationToken);
            if (existing is not null)
            {
                if (!StringComparer.Ordinal.Equals(existing.SourceFingerprint, sourceFingerprint))
                {
                    throw new InvalidOperationException(
                        $"The configured {supportedCurrency} Watch-Only Wallet Source differs from the source bound to the default Project.");
                }

                return;
            }

            var now = DateTimeOffset.UtcNow;
            dbContext.ProjectWatchOnlyWalletSources.Add(new Records.ProjectWatchOnlyWalletSourceRecord
            {
                ProjectId = ProjectDefaults.DefaultProjectId,
                SupportedCurrency = supportedCurrency,
                SourceFingerprint = sourceFingerprint,
                Network = source.Network,
                AddressType = source.AddressType,
                StartingIndex = source.StartingIndex,
                SourceReference = "legacy",
                Enabled = true,
                CreatedAt = now,
                UpdatedAt = now,
                Version = 1,
            });
        }
    }

    private static long ToWholeSeconds(TimeSpan value, string setting, bool allowZero = false)
    {
        var seconds = value.TotalSeconds;
        if (seconds != Math.Truncate(seconds) || seconds < (allowZero ? 0 : 1))
        {
            throw new InvalidOperationException($"{setting} must be a whole number of seconds.");
        }

        return checked((long)seconds);
    }

    private static string ComputeFingerprint(
        PaymentApplicationOptions paymentOptions,
        PaymentAddressOptions addressOptions)
    {
        var canonical = string.Join(
            '\n',
            paymentOptions.PaymentExpiration.Ticks.ToString(CultureInfo.InvariantCulture),
            paymentOptions.LateAcceptanceWindow.Ticks.ToString(CultureInfo.InvariantCulture),
            paymentOptions.PaymentTolerancePercent.ToString(CultureInfo.InvariantCulture),
            paymentOptions.BtcConfirmationRequirement.ToString(CultureInfo.InvariantCulture),
            paymentOptions.LtcConfirmationRequirement.ToString(CultureInfo.InvariantCulture),
            paymentOptions.EthConfirmationRequirement.ToString(CultureInfo.InvariantCulture),
            paymentOptions.BtcReorgMonitoringDepth.ToString(CultureInfo.InvariantCulture),
            paymentOptions.LtcReorgMonitoringDepth.ToString(CultureInfo.InvariantCulture),
            paymentOptions.EthReorgMonitoringDepth.ToString(CultureInfo.InvariantCulture),
            addressOptions.NativeEthLowCapacityThreshold.ToString(CultureInfo.InvariantCulture));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }
}
