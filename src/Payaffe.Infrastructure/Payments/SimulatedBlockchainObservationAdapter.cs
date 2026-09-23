using Payaffe.Application.Payments;
using Payaffe.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Payaffe.Infrastructure.Payments;

/// <summary>
/// The Blockchain Observation of a Test Mode installation (ADR 0033): it
/// reports the Simulated Transactions recorded for a Payment as if a provider
/// had seen them on-chain.
/// </summary>
/// <remarks>
/// A Simulated Transaction is reported unconfirmed on the first poll that
/// finds it and fully confirmed on every poll after that, so an integration
/// sees `payment.observed` before `payment.completed`, as it would with a
/// real transaction. Fully confirmed means the Payment's Confirmation
/// Requirement plus its Reorg Monitoring Depth, which is also what takes it
/// out of reorg monitoring: a simulation has no reorganisations to report.
/// </remarks>
public sealed class SimulatedBlockchainObservationAdapter(
    PayaffeDbContext dbContext,
    IClock clock) : IBlockchainObservationAdapter
{
    public const string ProviderName = "simulated";

    /// <summary>
    /// For a Payment without snapshotted requirements, which only a Payment
    /// selected before those snapshots existed can be.
    /// </summary>
    private const int FallbackConfirmations = 100;

    public Task StartWatchingAsync(
        BlockchainObservationTarget target,
        CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public async Task<IReadOnlyList<BlockchainObservation>> PollAsync(
        BlockchainObservationTarget target,
        CancellationToken cancellationToken)
    {
        var transactions = await dbContext.SimulatedTransactions
            .Where(transaction =>
                transaction.PaymentId == target.PaymentId &&
                (target.ProjectId == Guid.Empty || transaction.ProjectId == target.ProjectId) &&
                transaction.SupportedCurrency == target.SupportedCurrency &&
                transaction.PaymentAddress == target.PaymentAddress)
            .OrderBy(transaction => transaction.CreatedAt)
            .ThenBy(transaction => transaction.Id)
            .ToListAsync(cancellationToken);
        if (transactions.Count == 0)
        {
            return [];
        }

        var requirements = await dbContext.Payments
            .AsNoTracking()
            .Where(payment => payment.Id == target.PaymentId)
            .Select(payment => new { payment.ConfirmationRequirement, payment.ReorgMonitoringDepth })
            .SingleOrDefaultAsync(cancellationToken);
        var confirmed = requirements?.ConfirmationRequirement is { } required
            ? required + Math.Max(0, requirements.ReorgMonitoringDepth ?? 0)
            : FallbackConfirmations;

        var now = clock.UtcNow;
        var observations = new List<BlockchainObservation>(transactions.Count);
        foreach (var transaction in transactions)
        {
            var firstReport = transaction.FirstReportedAt is null;
            transaction.FirstReportedAt ??= now;
            observations.Add(new BlockchainObservation(
                transaction.TransactionHash,
                transaction.Amount,
                transaction.ObservedAt,
                firstReport ? 0 : confirmed,
                ProviderName,
                transaction.Id.ToString("D")));
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return observations;
    }

    public Task<bool> IsObservationAvailableAsync(
        string supportedCurrency,
        CancellationToken cancellationToken) =>
        Task.FromResult(supportedCurrency is "BTC" or "LTC" or "ETH");
}
