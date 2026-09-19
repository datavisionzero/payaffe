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
    /// <summary>
    /// The external chain of a BIP44-style account: `account/0` holds the
    /// addresses a payer is shown, `account/1` the change a wallet keeps to
    /// itself. payaffe hands out receiving addresses and so only ever uses the
    /// former.
    /// </summary>
    private const uint ExternalChainIndex = 0;

    private readonly PaymentAddressOptions _options = options.Value;

    public async Task<bool> IsAddressAvailableAsync(
        Guid projectId,
        string supportedCurrency,
        CancellationToken cancellationToken)
    {
        return supportedCurrency switch
        {
            "BTC" or "LTC" => await ResolveWatchOnlySourceAsync(
                projectId,
                supportedCurrency,
                cancellationToken) is not null,
            "ETH" => await dbContext.NativeEthAddresses
                .AnyAsync(address => address.ProjectId == projectId && address.Status == "unused", cancellationToken),
            _ => false,
        };
    }

    public async Task<PaymentAddressAssignment?> AssignAsync(
        Guid projectId,
        Guid paymentId,
        string supportedCurrency,
        CancellationToken cancellationToken)
    {
        var existing = await dbContext.PaymentAddressAssignments
            .AsNoTracking()
            .SingleOrDefaultAsync(
                assignment => assignment.ProjectId == projectId && assignment.PaymentId == paymentId,
                cancellationToken);
        if (existing is not null)
        {
            return StringComparer.Ordinal.Equals(existing.SupportedCurrency, supportedCurrency)
                ? new PaymentAddressAssignment(
                    existing.SupportedCurrency,
                    existing.PaymentAddress,
                    existing.Network,
                    existing.ChainId)
                : null;
        }

        return supportedCurrency switch
        {
            "BTC" => await AssignWatchOnlyAsync(
                paymentId,
                projectId,
                supportedCurrency,
                cancellationToken),
            "LTC" => await AssignWatchOnlyAsync(
                paymentId,
                projectId,
                supportedCurrency,
                cancellationToken),
            "ETH" => await AssignNativeEthAsync(projectId, paymentId, cancellationToken),
            _ => null,
        };
    }

    private async Task<PaymentAddressAssignment?> AssignWatchOnlyAsync(
        Guid paymentId,
        Guid projectId,
        string supportedCurrency,
        CancellationToken cancellationToken)
    {
        var source = await ResolveWatchOnlySourceAsync(projectId, supportedCurrency, cancellationToken);
        if (source is null)
        {
            return null;
        }

        await using var transaction = dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(cancellationToken)
            : null;
        await AcquireSourceLockAsync(supportedCurrency, source.SourceFingerprint, cancellationToken);

        var existing = await dbContext.PaymentAddressAssignments
            .SingleOrDefaultAsync(
                assignment => assignment.ProjectId == projectId && assignment.PaymentId == paymentId,
                cancellationToken);
        if (existing is not null)
        {
            return StringComparer.Ordinal.Equals(existing.SupportedCurrency, supportedCurrency)
                ? new PaymentAddressAssignment(
                    existing.SupportedCurrency,
                    existing.PaymentAddress,
                    existing.Network,
                    existing.ChainId)
                : null;
        }

        var now = clock.UtcNow;
        var cursor = await dbContext.WatchOnlyWalletCursors
            .SingleOrDefaultAsync(
                candidate => candidate.SupportedCurrency == supportedCurrency &&
                             candidate.SourceFingerprint == source.SourceFingerprint,
                cancellationToken);
        if (cursor is null)
        {
            cursor = new WatchOnlyWalletCursorRecord
            {
                SupportedCurrency = supportedCurrency,
                SourceFingerprint = source.SourceFingerprint,
                NextDerivationIndex = source.StartingIndex,
                CreatedAt = now,
                UpdatedAt = now,
                Version = 1,
            };
            dbContext.WatchOnlyWalletCursors.Add(cursor);
        }
        if (cursor.NextDerivationIndex > 2_147_483_647)
        {
            return null;
        }

        var network = PaymentAddressOptionsValidator.ResolveNetwork(
            supportedCurrency,
            source.Network);
        var extendedPublicKey = ExtPubKey.Parse(source.ExtendedPublicKey, network);
        // The configured key is the account node, `m/purpose'/coin'/account'`,
        // which is what every wallet exports and what the operations guide asks
        // for. Addresses live one level below it on the external chain, so the
        // index is derived from `account/0` and not from the account itself.
        // Deriving it directly would place payments on `account/i` -- the level
        // BIP44 reserves for the chain -- and no wallet restored from the seed
        // scans there.
        var derivedPublicKey = extendedPublicKey
            .Derive(ExternalChainIndex)
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
            ProjectId = projectId,
            PaymentId = paymentId,
            SupportedCurrency = supportedCurrency,
            PaymentAddress = paymentAddress,
            Network = source.Network,
            ChainId = null,
            SourceFingerprint = source.SourceFingerprint,
            DerivationIndex = cursor.NextDerivationIndex - 1,
            AssignedAt = now,
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        return new PaymentAddressAssignment(
            supportedCurrency,
            paymentAddress,
            source.Network);
    }

    private async Task<PaymentAddressAssignment?> AssignNativeEthAsync(
        Guid projectId,
        Guid paymentId,
        CancellationToken cancellationToken)
    {
        await using var transaction = dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(cancellationToken)
            : null;
        await AcquireSourceLockAsync("ETH", projectId.ToString("D"), cancellationToken);

        var existing = await dbContext.PaymentAddressAssignments
            .SingleOrDefaultAsync(
                assignment => assignment.ProjectId == projectId && assignment.PaymentId == paymentId,
                cancellationToken);
        if (existing is not null)
        {
            return StringComparer.Ordinal.Equals(existing.SupportedCurrency, "ETH")
                ? new PaymentAddressAssignment(
                    "ETH",
                    existing.PaymentAddress,
                    existing.Network,
                    existing.ChainId)
                : null;
        }

        var poolAddress = await dbContext.NativeEthAddresses
            .Where(address => address.ProjectId == projectId && address.Status == "unused")
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
            ProjectId = projectId,
            PaymentId = paymentId,
            SupportedCurrency = "ETH",
            PaymentAddress = poolAddress.Address,
            Network = _options.NativeEthNetwork,
            ChainId = _options.NativeEthChainId,
            AssignedAt = now,
        });
        await dbContext.SaveChangesAsync(cancellationToken);
        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        return new PaymentAddressAssignment(
            "ETH",
            poolAddress.Address,
            _options.NativeEthNetwork,
            _options.NativeEthChainId);
    }

    private Task AcquireSourceLockAsync(
        string supportedCurrency,
        string sourceFingerprint,
        CancellationToken cancellationToken)
    {
        return dbContext.Database.IsNpgsql()
            ? dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"select pg_advisory_xact_lock(hashtext({'d' + supportedCurrency + sourceFingerprint}))",
                cancellationToken)
            : Task.CompletedTask;
    }

    private async Task<ResolvedWatchOnlySource?> ResolveWatchOnlySourceAsync(
        Guid projectId,
        string supportedCurrency,
        CancellationToken cancellationToken)
    {
        var binding = await dbContext.ProjectWatchOnlyWalletSources
            .AsNoTracking()
            .SingleOrDefaultAsync(
                source => source.ProjectId == projectId &&
                          source.SupportedCurrency == supportedCurrency &&
                          source.Enabled,
                cancellationToken);

        WatchOnlyWalletSourceOptions? legacy = supportedCurrency switch
        {
            "BTC" => _options.Btc,
            "LTC" => _options.Ltc,
            _ => null,
        };
        if (binding is null)
        {
            if (projectId != ProjectDefaults.DefaultProjectId ||
                legacy is null ||
                !legacy.Enabled ||
                string.IsNullOrWhiteSpace(legacy.ExtendedPublicKey))
            {
                return null;
            }

            var legacyFingerprint = WatchOnlyWalletSourceFingerprint.Compute(
                supportedCurrency,
                legacy.Network,
                legacy.AddressType,
                legacy.ExtendedPublicKey);
            return new ResolvedWatchOnlySource(
                legacyFingerprint,
                legacy.Network,
                legacy.AddressType,
                legacy.StartingIndex,
                legacy.ExtendedPublicKey);
        }

        var extendedPublicKey = binding.SourceReference switch
        {
            "legacy" when legacy is not null => legacy.ExtendedPublicKey,
            var reference when reference.StartsWith("xpub:", StringComparison.Ordinal) => reference[5..],
            _ => null,
        };
        if (string.IsNullOrWhiteSpace(extendedPublicKey))
        {
            return null;
        }

        var fingerprint = WatchOnlyWalletSourceFingerprint.Compute(
            supportedCurrency,
            binding.Network,
            binding.AddressType,
            extendedPublicKey);
        if (!StringComparer.Ordinal.Equals(fingerprint, binding.SourceFingerprint))
        {
            throw new InvalidOperationException(
                $"The configured {supportedCurrency} Watch-Only Wallet Source does not match its persisted fingerprint.");
        }

        return new ResolvedWatchOnlySource(
            fingerprint,
            binding.Network,
            binding.AddressType,
            binding.StartingIndex,
            extendedPublicKey);
    }

    private sealed record ResolvedWatchOnlySource(
        string SourceFingerprint,
        string Network,
        string AddressType,
        long StartingIndex,
        string ExtendedPublicKey);
}
