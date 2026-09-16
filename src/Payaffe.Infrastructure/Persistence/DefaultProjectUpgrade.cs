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

            return;
        }

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

        await dbContext.SaveChangesAsync(cancellationToken);
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
