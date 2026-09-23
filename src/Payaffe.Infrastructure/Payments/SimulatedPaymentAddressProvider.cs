using System.Security.Cryptography;
using System.Text;
using Payaffe.Application.Payments;
using Payaffe.Infrastructure.Persistence;
using Payaffe.Infrastructure.Persistence.Records;
using Microsoft.EntityFrameworkCore;
using NBitcoin;
using NBitcoin.Altcoins;

namespace Payaffe.Infrastructure.Payments;

/// <summary>
/// The Payment Addresses of a Test Mode installation (ADR 0033): every
/// Supported Currency in every Project, with no Watch-Only Wallet Source and
/// no imported pool.
/// </summary>
/// <remarks>
/// An address is a hash of the Payment it belongs to, so it is unique per
/// Payment and the same on every read, and nobody holds a key for it. BTC and
/// LTC addresses are testnet addresses and native ETH instructions name the
/// Sepolia chain, so a wallet asked to pay one on mainnet refuses.
/// </remarks>
public sealed class SimulatedPaymentAddressProvider(
    PayaffeDbContext dbContext,
    IClock clock)
    : IPaymentAddressProvider
{
    public const string Network = "testnet";

    /// <summary>Sepolia, the Ethereum test network wallets know by default.</summary>
    public const long NativeEthChainId = 11_155_111;

    private const string SourceFingerprint = "simulated";

    public Task<bool> IsAddressAvailableAsync(
        Guid projectId,
        string supportedCurrency,
        CancellationToken cancellationToken) =>
        Task.FromResult(supportedCurrency is "BTC" or "LTC" or "ETH");

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

        var address = CreateAddress(paymentId, supportedCurrency);
        if (address is null)
        {
            return null;
        }

        var chainId = supportedCurrency == "ETH" ? NativeEthChainId : (long?)null;
        dbContext.PaymentAddressAssignments.Add(new PaymentAddressAssignmentRecord
        {
            ProjectId = projectId,
            PaymentId = paymentId,
            SupportedCurrency = supportedCurrency,
            PaymentAddress = address,
            Network = Network,
            ChainId = chainId,
            SourceFingerprint = SourceFingerprint,
            AssignedAt = clock.UtcNow,
        });
        await dbContext.SaveChangesAsync(cancellationToken);

        return new PaymentAddressAssignment(supportedCurrency, address, Network, chainId);
    }

    public static string? CreateAddress(Guid paymentId, string supportedCurrency)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes($"payaffe-simulated-address:{supportedCurrency}:{paymentId:D}"));
        var program = new WitKeyId(digest[..20]);
        return supportedCurrency switch
        {
            "BTC" => program.GetAddress(NBitcoin.Network.TestNet).ToString(),
            "LTC" => program.GetAddress(Litecoin.Instance.Testnet).ToString(),
            "ETH" => "0x" + Convert.ToHexStringLower(digest[..20]),
            _ => null,
        };
    }
}
