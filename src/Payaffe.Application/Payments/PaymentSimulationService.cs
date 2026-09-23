using System.Security.Cryptography;
using System.Text.Json;
using Payaffe.Domain.Payments;

namespace Payaffe.Application.Payments;

/// <summary>
/// Records Simulated Transactions for a Test Mode installation (ADR 0033).
/// </summary>
/// <remarks>
/// Registered only in Test Mode. Recording one changes no Payment Status by
/// itself: the simulated Blockchain Observation reports it on its next poll,
/// and from there the Payment runs the same lifecycle a real transaction
/// would. That is the point of the simulation — an integration is tested
/// against the path it will meet in production.
/// </remarks>
public sealed class PaymentSimulationService(
    ISimulatedTransactionStore store,
    IClock clock)
{
    public const string EventType = "payment.simulated_transaction_recorded";

    public async Task<RecordSimulatedTransactionResult> RecordSimulatedTransactionAsync(
        RecordSimulatedTransactionCommand command,
        CancellationToken cancellationToken)
    {
        if (command.PaymentId == Guid.Empty)
        {
            throw new DomainRuleException("Payment identifier is required.", "payment_id.required");
        }

        var payment = await store.FindPaymentAsync(command.ProjectId, command.PaymentId, cancellationToken);
        if (payment is null)
        {
            return RecordSimulatedTransactionResult.PaymentNotFound();
        }

        if (payment.Status is not ("waiting_for_payment" or "observed") ||
            payment.SelectedCurrency is null ||
            payment.PaymentAddress is null ||
            payment.ExpectedCryptoAmount is null)
        {
            return RecordSimulatedTransactionResult.PaymentNotReady();
        }

        var amount = NormalizeAmount(payment.SelectedCurrency, command.Amount ?? payment.ExpectedCryptoAmount);
        var recordedAt = clock.UtcNow;
        var transaction = new SimulatedTransactionDraft(
            Guid.NewGuid(),
            command.ProjectId,
            payment.PaymentId,
            payment.SelectedCurrency,
            payment.PaymentAddress,
            Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32)),
            amount,
            recordedAt);
        await store.AddAsync(
            transaction,
            new PaymentEventDraft(
                Guid.NewGuid(),
                payment.PaymentId,
                EventType,
                recordedAt,
                JsonSerializer.Serialize(new
                {
                    transaction_hash = transaction.TransactionHash,
                    amount = transaction.Amount,
                })),
            cancellationToken);

        return RecordSimulatedTransactionResult.Recorded(new SimulatedTransactionResponse(
            transaction.PaymentId,
            transaction.SupportedCurrency,
            transaction.PaymentAddress,
            transaction.TransactionHash,
            transaction.Amount,
            transaction.RecordedAt));
    }

    private static string NormalizeAmount(string supportedCurrency, string amount)
    {
        var trimmed = amount.Trim();
        string atomic;
        try
        {
            atomic = PaymentInstructionFactory.ToAtomicAmount(supportedCurrency, trimmed);
        }
        catch (InvalidOperationException)
        {
            throw new DomainRuleException(
                $"Amount must be a plain decimal in {supportedCurrency} with at most its precision, such as 0.0004.",
                "amount.invalid");
        }

        return atomic == "0"
            ? throw new DomainRuleException("Amount must be greater than zero.", "amount.not_positive")
            : trimmed;
    }
}

public sealed record RecordSimulatedTransactionCommand(Guid ProjectId, Guid PaymentId, string? Amount);

public sealed record SimulatedTransactionResponse(
    Guid PaymentId,
    string SupportedCurrency,
    string PaymentAddress,
    string TransactionHash,
    string Amount,
    DateTimeOffset RecordedAt);

public sealed record RecordSimulatedTransactionResult(
    RecordSimulatedTransactionResultKind Kind,
    SimulatedTransactionResponse? Transaction)
{
    public static RecordSimulatedTransactionResult Recorded(SimulatedTransactionResponse transaction) =>
        new(RecordSimulatedTransactionResultKind.Recorded, transaction);

    public static RecordSimulatedTransactionResult PaymentNotFound() =>
        new(RecordSimulatedTransactionResultKind.PaymentNotFound, Transaction: null);

    public static RecordSimulatedTransactionResult PaymentNotReady() =>
        new(RecordSimulatedTransactionResultKind.PaymentNotReady, Transaction: null);
}

public enum RecordSimulatedTransactionResultKind
{
    Recorded,
    PaymentNotFound,

    /// <summary>
    /// The Payment has no Payment Instruction yet, or is past the point where
    /// a transaction could change it.
    /// </summary>
    PaymentNotReady,
}

public interface ISimulatedTransactionStore
{
    Task<SimulationTargetPayment?> FindPaymentAsync(
        Guid projectId,
        Guid paymentId,
        CancellationToken cancellationToken);

    Task AddAsync(
        SimulatedTransactionDraft transaction,
        PaymentEventDraft paymentEvent,
        CancellationToken cancellationToken);
}

public sealed record SimulationTargetPayment(
    Guid PaymentId,
    string Status,
    string? SelectedCurrency,
    string? PaymentAddress,
    string? ExpectedCryptoAmount);

public sealed record SimulatedTransactionDraft(
    Guid Id,
    Guid ProjectId,
    Guid PaymentId,
    string SupportedCurrency,
    string PaymentAddress,
    string TransactionHash,
    string Amount,
    DateTimeOffset RecordedAt);
