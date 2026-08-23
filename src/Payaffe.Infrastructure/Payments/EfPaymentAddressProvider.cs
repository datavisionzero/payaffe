using System.Security.Cryptography;
using System.Text;
using Payaffe.Application.Payments;
using Payaffe.Infrastructure.Persistence;
using Payaffe.Infrastructure.Persistence.Records;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NBitcoin;

namespace Payaffe.Infrastructure.Payments;

public sealed class EfPaymentAddressProvider(
    PayaffeDbContext dbContext,
    IClock clock,
    IOptions<PaymentAddressOptions> options)
    : IPaymentAddressProvider
{
    private readonly PaymentAddressOptions _options = options.Value;

    public async Task<bool> IsAddressAvailableAsync(
        string supportedCurrency,
        CancellationToken cancellationToken)
    {
        return supportedCurrency switch
        {
            "BTC" => _options.Btc.Enabled,
            "LTC" => _options.Ltc.Enabled,
            "ETH" => await dbContext.NativeEthAddresses
                .AnyAsync(address => address.Status == "unused", cancellationToken),
            _ => false,
        };
    }

    public async Task<PaymentAddressAssignment?> AssignAsync(
        Guid paymentId,
        string supportedCurrency,
        CancellationToken cancellationToken)
    {
        var existing = await dbContext.PaymentAddressAssignments
            .AsNoTracking()
            .SingleOrDefaultAsync(
                assignment => assignment.PaymentId == paymentId,
                cancellationToken);
        if (existing is not null)
        {
            return StringComparer.Ordinal.Equals(existing.SupportedCurrency, supportedCurrency)
                ? new PaymentAddressAssignment(existing.SupportedCurrency, existing.PaymentAddress)
                : null;
        }

        return supportedCurrency switch
        {
            "BTC" => await AssignWatchOnlyAsync(
                paymentId,
                supportedCurrency,
                _options.Btc,
                cancellationToken),
            "LTC" => await AssignWatchOnlyAsync(
                paymentId,
                supportedCurrency,
                _options.Ltc,
                cancellationToken),
            "ETH" => await AssignNativeEthAsync(paymentId, cancellationToken),
            _ => null,
        };
    }

    private async Task<PaymentAddressAssignment?> AssignWatchOnlyAsync(
        Guid paymentId,
        string supportedCurrency,
        WatchOnlyWalletSourceOptions source,
        CancellationToken cancellationToken)
    {
        if (!source.Enabled || string.IsNullOrWhiteSpace(source.ExtendedPublicKey))
        {
            return null;
        }

        await using var transaction = dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(cancellationToken)
            : null;
        await AcquireCurrencyLockAsync(supportedCurrency, cancellationToken);

        var existing = await dbContext.PaymentAddressAssignments
            .SingleOrDefaultAsync(
                assignment => assignment.PaymentId == paymentId,
                cancellationToken);
        if (existing is not null)
        {
            return StringComparer.Ordinal.Equals(existing.SupportedCurrency, supportedCurrency)
                ? new PaymentAddressAssignment(existing.SupportedCurrency, existing.PaymentAddress)
                : null;
        }

        var now = clock.UtcNow;
        var fingerprint = ComputeFingerprint(supportedCurrency, source);
        var cursor = await dbContext.WatchOnlyWalletCursors
            .SingleOrDefaultAsync(
                candidate => candidate.SupportedCurrency == supportedCurrency,
                cancellationToken);
        if (cursor is null)
        {
            cursor = new WatchOnlyWalletCursorRecord
            {
                SupportedCurrency = supportedCurrency,
                SourceFingerprint = fingerprint,
                NextDerivationIndex = source.StartingIndex,
                CreatedAt = now,
                UpdatedAt = now,
                Version = 1,
            };
            dbContext.WatchOnlyWalletCursors.Add(cursor);
        }
        else if (!CryptographicOperations.FixedTimeEquals(
                     Convert.FromHexString(cursor.SourceFingerprint),
                     Convert.FromHexString(fingerprint)))
        {
            throw new InvalidOperationException(
                $"The configured {supportedCurrency} Watch-Only Wallet Source does not match its persisted derivation cursor.");
        }

        if (cursor.NextDerivationIndex > 2_147_483_647)
        {
            return null;
        }

        var network = PaymentAddressOptionsValidator.ResolveNetwork(
            supportedCurrency,
            source.Network);
        var extendedPublicKey = ExtPubKey.Parse(source.ExtendedPublicKey, network);
        var derivedPublicKey = extendedPublicKey
            .Derive((uint)cursor.NextDerivationIndex)
            .PubKey;
        var addressType = source.AddressType == "legacy"
            ? ScriptPubKeyType.Legacy
            : ScriptPubKeyType.Segwit;
        var paymentAddress = derivedPublicKey.GetAddress(addressType, network).ToString();

        cursor.NextDerivationIndex++;
        cursor.UpdatedAt = now;
        cursor.Version++;
        dbContext.PaymentAddressAssignments.Add(new PaymentAddressAssignmentRecord
        {
            PaymentId = paymentId,
            SupportedCurrency = supportedCurrency,
            PaymentAddress = paymentAddress,
            AssignedAt = now,
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        return new PaymentAddressAssignment(supportedCurrency, paymentAddress);
    }

    private async Task<PaymentAddressAssignment?> AssignNativeEthAsync(
        Guid paymentId,
        CancellationToken cancellationToken)
    {
        await using var transaction = dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(cancellationToken)
            : null;
        await AcquireCurrencyLockAsync("ETH", cancellationToken);

        var existing = await dbContext.PaymentAddressAssignments
            .SingleOrDefaultAsync(
                assignment => assignment.PaymentId == paymentId,
                cancellationToken);
        if (existing is not null)
        {
            return StringComparer.Ordinal.Equals(existing.SupportedCurrency, "ETH")
                ? new PaymentAddressAssignment("ETH", existing.PaymentAddress)
                : null;
        }

        var poolAddress = await dbContext.NativeEthAddresses
            .Where(address => address.Status == "unused")
            .OrderBy(address => address.CreatedAt)
            .ThenBy(address => address.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (poolAddress is null)
        {
            return null;
        }

        var now = clock.UtcNow;
        poolAddress.Status = "assigned";
        poolAddress.AssignedPaymentId = paymentId;
        poolAddress.AssignedAt = now;
        poolAddress.UpdatedAt = now;
        poolAddress.Version++;
        dbContext.PaymentAddressAssignments.Add(new PaymentAddressAssignmentRecord
        {
            PaymentId = paymentId,
            SupportedCurrency = "ETH",
            PaymentAddress = poolAddress.Address,
            AssignedAt = now,
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        return new PaymentAddressAssignment("ETH", poolAddress.Address);
    }

    private Task AcquireCurrencyLockAsync(
        string supportedCurrency,
        CancellationToken cancellationToken)
    {
        return dbContext.Database.IsNpgsql()
            ? dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"select pg_advisory_xact_lock(hashtext({'d' + supportedCurrency}))",
                cancellationToken)
            : Task.CompletedTask;
    }

    private static string ComputeFingerprint(
        string supportedCurrency,
        WatchOnlyWalletSourceOptions source)
    {
        var bytes = Encoding.UTF8.GetBytes(
            $"{supportedCurrency}\n{source.Network}\n{source.AddressType}\n{source.ExtendedPublicKey}");
        return Convert.ToHexString(SHA256.HashData(bytes));
    }
}
