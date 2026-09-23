using Payaffe.Application.Payments;
using Payaffe.Infrastructure.Persistence.Records;
using Microsoft.EntityFrameworkCore;

namespace Payaffe.Infrastructure.Persistence;

public sealed class EfSimulatedTransactionStore(PayaffeDbContext dbContext) : ISimulatedTransactionStore
{
    public Task<SimulationTargetPayment?> FindPaymentAsync(
        Guid projectId,
        Guid paymentId,
        CancellationToken cancellationToken) =>
        dbContext.Payments
            .AsNoTracking()
            .Where(payment => payment.ProjectId == projectId && payment.Id == paymentId)
            .Select(payment => new SimulationTargetPayment(
                payment.Id,
                payment.Status,
                payment.SelectedCurrency,
                payment.PaymentAddress,
                payment.ExpectedCryptoAmount))
            .SingleOrDefaultAsync(cancellationToken);

    public Task<SimulatedTransactionResponse?> FindByIdempotencyKeyAsync(
        Guid projectId,
        Guid paymentId,
        string idempotencyKey,
        CancellationToken cancellationToken) =>
        dbContext.SimulatedTransactions
            .AsNoTracking()
            .Where(transaction =>
                transaction.ProjectId == projectId &&
                transaction.PaymentId == paymentId &&
                transaction.IdempotencyKey == idempotencyKey)
            .Select(transaction => new SimulatedTransactionResponse(
                transaction.PaymentId,
                transaction.SupportedCurrency,
                transaction.PaymentAddress,
                transaction.TransactionHash,
                transaction.Amount,
                transaction.CreatedAt))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task AddAsync(
        SimulatedTransactionDraft transaction,
        PaymentEventDraft paymentEvent,
        CancellationToken cancellationToken)
    {
        // One SaveChanges is one transaction: the Simulated Transaction and the
        // history entry that explains it are written together or not at all.
        dbContext.SimulatedTransactions.Add(new SimulatedTransactionRecord
        {
            ProjectId = transaction.ProjectId,
            Id = transaction.Id,
            PaymentId = transaction.PaymentId,
            SupportedCurrency = transaction.SupportedCurrency,
            PaymentAddress = transaction.PaymentAddress,
            TransactionHash = transaction.TransactionHash,
            Amount = transaction.Amount,
            IdempotencyKey = transaction.IdempotencyKey,
            ObservedAt = transaction.RecordedAt,
            CreatedAt = transaction.RecordedAt,
        });
        dbContext.PaymentEventHistory.Add(new PaymentEventHistoryRecord
        {
            ProjectId = transaction.ProjectId,
            Id = paymentEvent.Id,
            PaymentId = paymentEvent.PaymentId,
            EventType = paymentEvent.EventType,
            OccurredAt = paymentEvent.OccurredAt,
            Details = paymentEvent.Details,
        });
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
