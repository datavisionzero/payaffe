using System.Globalization;
using Payaffe.Application.Payments;
using Payaffe.Infrastructure.Persistence.Records;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Payaffe.Infrastructure.Persistence;

public sealed class EfPaymentStore(PayaffeDbContext dbContext) : IPaymentStore
{
    public async Task<CreatePaymentStoreResult> CreateAsync(
        Guid integrationApiCredentialId,
        string idempotencyKey,
        string requestHash,
        PaymentDraft payment,
        IReadOnlyCollection<PaymentOptionDraft> paymentOptions,
        IReadOnlyCollection<PaymentEventDraft> paymentEvents,
        IReadOnlyCollection<WebhookOutboxEventDraft> webhookEvents,
        CancellationToken cancellationToken)
    {
        var credentialContext = await (
                from credential in dbContext.IntegrationApiCredentials.AsNoTracking()
                join project in dbContext.Projects.AsNoTracking() on credential.ProjectId equals project.Id
                where credential.Id == integrationApiCredentialId && credential.Status == "active"
                select new { credential.ProjectId, ProjectStatus = project.Status })
            .SingleOrDefaultAsync(cancellationToken);
        if (credentialContext is null || credentialContext.ProjectStatus != "active")
        {
            return CreatePaymentStoreResult.ProjectUnavailable();
        }

        var projectId = credentialContext.ProjectId;
        var existingIdempotency = await dbContext.PaymentCreationIdempotency
            .AsNoTracking()
            .SingleOrDefaultAsync(
                record => record.ProjectId == projectId &&
                          record.IntegrationApiCredentialId == integrationApiCredentialId &&
                          record.IdempotencyKey == idempotencyKey,
                cancellationToken);

        if (existingIdempotency is not null)
        {
            if (!StringComparer.Ordinal.Equals(existingIdempotency.RequestHash, requestHash))
            {
                return CreatePaymentStoreResult.IdempotencyConflict();
            }

            var existingPayment = await FindAsync(
                integrationApiCredentialId,
                existingIdempotency.PaymentId,
                cancellationToken);

            return existingPayment is null
                ? throw new InvalidOperationException("Idempotency record points to a missing Payment.")
                : CreatePaymentStoreResult.Existing(existingPayment);
        }

        await using var transaction = await BeginTransactionIfRelationalAsync(cancellationToken);

        dbContext.Payments.Add(ToRecord(projectId, payment));
        dbContext.PaymentOptions.AddRange(paymentOptions.Select(option => ToRecord(projectId, option)));
        dbContext.PaymentEventHistory.AddRange(paymentEvents.Select(paymentEvent => ToRecord(projectId, paymentEvent)));
        dbContext.WebhookOutboxEvents.AddRange(webhookEvents.Select(webhookEvent => ToRecord(projectId, webhookEvent)));
        dbContext.PaymentCreationIdempotency.Add(new PaymentCreationIdempotencyRecord
        {
            ProjectId = projectId,
            IntegrationApiCredentialId = integrationApiCredentialId,
            IdempotencyKey = idempotencyKey,
            RequestHash = requestHash,
            PaymentId = payment.Id,
            CreatedAt = payment.CreatedAt,
        });

        await dbContext.SaveChangesAsync(cancellationToken);

        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        return CreatePaymentStoreResult.Created(ToReadModel(projectId, payment, paymentOptions));
    }

    public async Task<PaymentReadModel?> FindAsync(
        Guid integrationApiCredentialId,
        Guid paymentId,
        CancellationToken cancellationToken)
    {
        var projectId = await dbContext.IntegrationApiCredentials
            .AsNoTracking()
            .Where(credential => credential.Id == integrationApiCredentialId && credential.Status == "active")
            .Select(credential => (Guid?)credential.ProjectId)
            .SingleOrDefaultAsync(cancellationToken);
        if (projectId is null)
        {
            return null;
        }

        var payment = await dbContext.Payments
            .AsNoTracking()
            .SingleOrDefaultAsync(
                candidate => candidate.Id == paymentId &&
                             candidate.ProjectId == projectId.Value,
                cancellationToken);

        return payment is null
            ? null
            : ToReadModel(
                payment,
                await CalculateObservedTotalAsync(payment.ProjectId, payment.Id, cancellationToken),
                await LoadPaymentOptionsAsync(payment.ProjectId, payment.Id, cancellationToken),
                await LoadRateLockAsync(payment.ProjectId, payment.Id, cancellationToken),
                await LoadPaymentInstructionAsync(payment.ProjectId, payment.Id, cancellationToken));
    }

    public async Task<PaymentReadModel?> FindByPayerPageIdAsync(
        string payerPageId,
        CancellationToken cancellationToken)
    {
        var payment = await dbContext.Payments
            .AsNoTracking()
            .SingleOrDefaultAsync(
                candidate => candidate.PayerPageId == payerPageId,
                cancellationToken);

        return payment is null
            ? null
            : ToReadModel(
                payment,
                await CalculateObservedTotalAsync(payment.ProjectId, payment.Id, cancellationToken),
                await LoadPaymentOptionsAsync(payment.ProjectId, payment.Id, cancellationToken),
                await LoadRateLockAsync(payment.ProjectId, payment.Id, cancellationToken),
                await LoadPaymentInstructionAsync(payment.ProjectId, payment.Id, cancellationToken));
    }

    public async Task<IReadOnlyList<BlockchainObservationTarget>> ListBlockchainObservationTargetsAsync(
        DateTimeOffset observedUntil,
        TimeSpan observedConfirmationWait,
        int maxPayments,
        CancellationToken cancellationToken)
    {
        var confirmationWaitCutoff = observedUntil - observedConfirmationWait;
        var observedInTime = PaymentsWithTransactionObservedInTime();

        // Least recently polled first, never-polled before all others. A poll
        // that finds nothing changes no Payment, so ordering by anything the
        // poll does not write would pick the same Payments on every tick.
        var targets = await dbContext.Payments
            .AsNoTracking()
            .Where(payment =>
                ((payment.Status == "waiting_for_payment" && payment.LateAcceptanceEndsAt > observedUntil) ||
                 (payment.Status == "observed" &&
                  (payment.LateAcceptanceEndsAt > observedUntil ||
                   (payment.LateAcceptanceEndsAt > confirmationWaitCutoff && observedInTime.Contains(payment.Id))))) &&
                payment.SelectedCurrency != null &&
                payment.PaymentAddress != null &&
                payment.ExpectedCryptoAmount != null)
            .OrderBy(payment => payment.LastPolledAt.HasValue)
            .ThenBy(payment => payment.LastPolledAt)
            .ThenBy(payment => payment.Id)
            .Take(maxPayments)
            .Select(payment => new BlockchainObservationTarget(
                payment.Id,
                payment.SelectedCurrency!,
                payment.PaymentAddress!,
                payment.ExpectedCryptoAmount!,
                payment.ProjectId))
            .ToListAsync(cancellationToken);

        // Stamped before the poll, so a target whose poll fails moves to the
        // back of the rotation as well.
        var paymentIds = targets.Select(target => target.PaymentId).ToArray();
        if (dbContext.Database.IsRelational())
        {
            // Not a Payment change: no version bump, so it never conflicts
            // with a concurrent lifecycle write.
            await dbContext.Payments
                .Where(payment => paymentIds.Contains(payment.Id))
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(payment => payment.LastPolledAt, observedUntil),
                    cancellationToken);
        }
        else
        {
            await DiscardChangesOnFailureAsync(async () =>
            {
                foreach (var payment in await dbContext.Payments
                             .Where(payment => paymentIds.Contains(payment.Id))
                             .ToListAsync(cancellationToken))
                {
                    payment.LastPolledAt = observedUntil;
                }

                return await dbContext.SaveChangesAsync(cancellationToken);
            });
        }

        return targets;
    }

    public async Task<IReadOnlyList<BlockchainReorgMonitoringTarget>> ListBlockchainReorgMonitoringTargetsAsync(
        ReorgMonitoringPolicyDraft monitoringPolicy,
        int maxTransactions,
        CancellationToken cancellationToken)
    {
        // A Reorg Alert does not end monitoring: a false alert must not hide a
        // later real reorganisation. Checked transactions are stamped before
        // the poll, so every monitored transaction takes its turn.
        var targets = await (
                from transaction in dbContext.MatchingBlockchainTransactions.AsNoTracking()
                join payment in dbContext.Payments.AsNoTracking()
                    on new { transaction.ProjectId, Id = transaction.PaymentId }
                    equals new { payment.ProjectId, payment.Id }
                where payment.Status == "completed" &&
                      transaction.ContributedToCompletion &&
                      payment.SelectedCurrency != null &&
                      payment.PaymentAddress != null &&
                      payment.ExpectedCryptoAmount != null &&
                      transaction.SupportedCurrency == payment.SelectedCurrency &&
                      transaction.PaymentAddress == payment.PaymentAddress &&
                      transaction.Confirmations <
                      (payment.ConfirmationRequirement ??
                       (transaction.SupportedCurrency == "BTC" ? monitoringPolicy.BtcRequiredConfirmations :
                        transaction.SupportedCurrency == "LTC" ? monitoringPolicy.LtcRequiredConfirmations :
                        monitoringPolicy.EthRequiredConfirmations)) +
                      (payment.ReorgMonitoringDepth ??
                       (transaction.SupportedCurrency == "BTC" ? monitoringPolicy.BtcMonitoringDepth :
                        transaction.SupportedCurrency == "LTC" ? monitoringPolicy.LtcMonitoringDepth :
                        monitoringPolicy.EthMonitoringDepth))
                orderby transaction.LastCheckedAt, transaction.Id
                select new
                {
                    transaction.Id,
                    Target = new BlockchainReorgMonitoringTarget(
                        payment.Id,
                        transaction.SupportedCurrency,
                        transaction.PaymentAddress,
                        payment.ExpectedCryptoAmount!,
                        transaction.TransactionHash,
                        transaction.Confirmations,
                        payment.ProjectId),
                })
            .Take(maxTransactions)
            .ToListAsync(cancellationToken);

        var transactionIds = targets.Select(target => target.Id).ToArray();
        var checkedAt = monitoringPolicy.CheckedAt;
        if (dbContext.Database.IsRelational())
        {
            await dbContext.MatchingBlockchainTransactions
                .Where(transaction => transactionIds.Contains(transaction.Id))
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(transaction => transaction.LastCheckedAt, checkedAt),
                    cancellationToken);
        }
        else
        {
            await DiscardChangesOnFailureAsync(async () =>
            {
                foreach (var transaction in await dbContext.MatchingBlockchainTransactions
                             .Where(transaction => transactionIds.Contains(transaction.Id))
                             .ToListAsync(cancellationToken))
                {
                    transaction.LastCheckedAt = checkedAt;
                }

                return await dbContext.SaveChangesAsync(cancellationToken);
            });
        }

        return targets.Select(target => target.Target).ToArray();
    }

    public Task<SelectCurrencyStoreResult> SelectCurrencyAsync(
        PaymentSelectionDraft selection,
        PaymentEventDraft paymentEvent,
        WebhookOutboxEventDraft webhookEvent,
        CancellationToken cancellationToken) =>
        DiscardChangesOnFailureAsync(() => SelectCurrencyCoreAsync(
            selection, paymentEvent, webhookEvent, cancellationToken));

    private async Task<SelectCurrencyStoreResult> SelectCurrencyCoreAsync(
        PaymentSelectionDraft selection,
        PaymentEventDraft paymentEvent,
        WebhookOutboxEventDraft webhookEvent,
        CancellationToken cancellationToken)
    {
        await using var transaction = await BeginTransactionIfRelationalAsync(cancellationToken);

        var payment = await dbContext.Payments
            .SingleOrDefaultAsync(
                candidate => candidate.Id == selection.PaymentId && candidate.ProjectId == selection.ProjectId,
                cancellationToken);
        if (payment is null)
        {
            return SelectCurrencyStoreResult.NotFound();
        }

        if (!StringComparer.Ordinal.Equals(payment.Status, "pending_currency_selection"))
        {
            return SelectCurrencyStoreResult.AlreadySelected(ToReadModel(payment));
        }

        payment.Status = "waiting_for_payment";
        payment.SelectedCurrency = selection.SupportedCurrency;
        payment.ExpectedCryptoAmount = selection.ExpectedCryptoAmount;
        payment.PaymentAddress = selection.PaymentAddress;
        payment.ConfirmationRequirement = selection.ConfirmationRequirement;
        payment.PaymentTolerancePercent = selection.PaymentTolerancePercent;
        payment.ReorgMonitoringDepth = selection.ReorgMonitoringDepth;
        payment.CurrencySelectedAt = selection.SelectedAt;
        payment.UpdatedAt = selection.SelectedAt;
        payment.Version++;

        var rateLock = new RateLockRecord
        {
            ProjectId = payment.ProjectId,
            PaymentId = selection.PaymentId,
            SupportedCurrency = selection.SupportedCurrency,
            FiatCurrency = payment.FiatCurrency,
            FiatAmountMinor = payment.FiatAmountMinor,
            ExpectedCryptoAmount = selection.ExpectedCryptoAmount,
            RateSource = selection.RateSource,
            RateValue = selection.RateValue,
            RateObservedAt = selection.RateObservedAt,
            CreatedAt = selection.SelectedAt,
        };
        dbContext.RateLocks.Add(rateLock);
        var addressAssignment = await dbContext.PaymentAddressAssignments
            .SingleOrDefaultAsync(
                assignment => assignment.ProjectId == payment.ProjectId &&
                              assignment.PaymentId == selection.PaymentId,
                cancellationToken);
        if (addressAssignment is null)
        {
            dbContext.PaymentAddressAssignments.Add(new PaymentAddressAssignmentRecord
            {
                ProjectId = payment.ProjectId,
                PaymentId = selection.PaymentId,
                SupportedCurrency = selection.SupportedCurrency,
                PaymentAddress = selection.PaymentAddress,
                Network = selection.Network,
                ChainId = selection.ChainId,
                AssignedAt = selection.SelectedAt,
            });
        }
        else if (!StringComparer.Ordinal.Equals(
                     addressAssignment.SupportedCurrency,
                     selection.SupportedCurrency) ||
                 !StringComparer.Ordinal.Equals(
                     addressAssignment.PaymentAddress,
                     selection.PaymentAddress) ||
                 !StringComparer.Ordinal.Equals(
                     addressAssignment.Network,
                     selection.Network) ||
                 addressAssignment.ChainId != selection.ChainId)
        {
            throw new InvalidOperationException(
                "The reserved Payment Address does not match the currency selection.");
        }
        dbContext.PaymentEventHistory.Add(ToRecord(payment.ProjectId, paymentEvent));
        dbContext.WebhookOutboxEvents.Add(ToRecord(payment.ProjectId, webhookEvent));

        await dbContext.SaveChangesAsync(cancellationToken);

        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        return SelectCurrencyStoreResult.Selected(ToReadModel(
            payment,
            rateLock: ToReadModel(rateLock),
            paymentInstruction: new PaymentInstructionReadModel(
                selection.SupportedCurrency,
                selection.Network,
                selection.ChainId,
                selection.ExpectedCryptoAmount,
                selection.PaymentAddress)));
    }

    public Task<RecordBlockchainObservationStoreResult> RecordBlockchainObservationAsync(
        BlockchainObservationDraft observation,
        PaymentCompletionPolicyDraft completionPolicy,
        PaymentEventDraft paymentEvent,
        WebhookOutboxEventDraft webhookEvent,
        PaymentEventDraft completedPaymentEvent,
        WebhookOutboxEventDraft completedWebhookEvent,
        CancellationToken cancellationToken) =>
        DiscardChangesOnFailureAsync(() => RecordBlockchainObservationCoreAsync(
            observation,
            completionPolicy,
            paymentEvent,
            webhookEvent,
            completedPaymentEvent,
            completedWebhookEvent,
            cancellationToken));

    private async Task<RecordBlockchainObservationStoreResult> RecordBlockchainObservationCoreAsync(
        BlockchainObservationDraft observation,
        PaymentCompletionPolicyDraft completionPolicy,
        PaymentEventDraft paymentEvent,
        WebhookOutboxEventDraft webhookEvent,
        PaymentEventDraft completedPaymentEvent,
        WebhookOutboxEventDraft completedWebhookEvent,
        CancellationToken cancellationToken)
    {
        await using var transaction = await BeginTransactionIfRelationalAsync(cancellationToken);

        var payment = await dbContext.Payments
            .SingleOrDefaultAsync(
                candidate => candidate.Id == observation.PaymentId &&
                             candidate.ProjectId == observation.ProjectId,
                cancellationToken);
        if (payment is null)
        {
            return RecordBlockchainObservationStoreResult.PaymentNotFound();
        }

        if (payment.Status is not ("waiting_for_payment" or "observed"))
        {
            return RecordBlockchainObservationStoreResult.PaymentNotReady();
        }

        if (!StringComparer.Ordinal.Equals(payment.SelectedCurrency, observation.SupportedCurrency) ||
            !StringComparer.Ordinal.Equals(payment.PaymentAddress, observation.PaymentAddress))
        {
            return RecordBlockchainObservationStoreResult.ObservationMismatch();
        }

        // An address can carry history: the derivation cursor restarts after a
        // database restore, and an extended key or an imported address may be
        // used elsewhere. A transaction observed well before currency
        // selection is not a payment of this Payment (ADR 0035). The
        // tolerance covers block timestamps that trail real time.
        if (payment.CurrencySelectedAt is { } currencySelectedAt &&
            observation.ObservedAt < currencySelectedAt - PreSelectionTolerance)
        {
            var ignored = await RecordAddressHistoryAsync(payment, observation, currencySelectedAt, cancellationToken);
            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            return ignored;
        }

        var existingTransaction = await dbContext.MatchingBlockchainTransactions
            .AsNoTracking()
            .SingleOrDefaultAsync(
                candidate => candidate.SupportedCurrency == observation.SupportedCurrency &&
                             candidate.TransactionHash == observation.TransactionHash &&
                             candidate.ProjectId == payment.ProjectId &&
                             candidate.PaymentId == payment.Id,
                cancellationToken);
        if (existingTransaction is not null)
        {
            return RecordBlockchainObservationStoreResult.AlreadyRecorded(
                ToReadModel(
                    payment,
                    await CalculateObservedTotalAsync(payment.ProjectId, payment.Id, cancellationToken)));
        }

        var observedTransaction = ToRecord(payment.ProjectId, observation);
        dbContext.MatchingBlockchainTransactions.Add(observedTransaction);

        var becameObserved = StringComparer.Ordinal.Equals(payment.Status, "waiting_for_payment");
        if (becameObserved)
        {
            payment.Status = "observed";
            payment.UpdatedAt = observation.UpdatedAt;
            payment.Version++;
            dbContext.PaymentEventHistory.Add(ToRecord(payment.ProjectId, paymentEvent));
            dbContext.WebhookOutboxEvents.Add(ToRecord(payment.ProjectId, webhookEvent with
            {
                IntegrationApiCredentialId = payment.IntegrationApiCredentialId,
            }));
        }

        completionPolicy = ResolveCompletionPolicy(payment, completionPolicy);
        var completion = await CalculateCompletionAsync(payment, observedTransaction, completionPolicy, cancellationToken);
        if (completion.IsCompleted)
        {
            await ApplyCompletionAsync(
                payment,
                observedTransaction,
                completion,
                completedPaymentEvent,
                completedWebhookEvent,
                observation.UpdatedAt,
                cancellationToken);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        var observedTotal = await CalculateObservedTotalAsync(payment.ProjectId, payment.Id, cancellationToken);

        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        return StringComparer.Ordinal.Equals(payment.Status, "completed")
            ? RecordBlockchainObservationStoreResult.Completed(ToReadModel(payment, observedTotal))
            : RecordBlockchainObservationStoreResult.Observed(ToReadModel(payment, observedTotal));
    }

    public Task<ExpireDuePaymentsStoreResult> ExpireDuePaymentsAsync(
        DateTimeOffset expiresBefore,
        TimeSpan observedConfirmationWait,
        int maxPayments,
        CancellationToken cancellationToken) =>
        DiscardChangesOnFailureAsync(() => ExpireDuePaymentsCoreAsync(
            expiresBefore, observedConfirmationWait, maxPayments, cancellationToken));

    /// <summary>
    /// Without a Payment Address nobody can pay, so a Payment still waiting for
    /// currency selection expires at Payment Expiration. An unpaid Payment
    /// expires when its Late Acceptance Window ends. An Observed Payment that
    /// saw a Matching Blockchain Transaction in time waits for its
    /// confirmations up to the confirmation wait after the window (ADR 0034).
    /// </summary>
    private async Task<ExpireDuePaymentsStoreResult> ExpireDuePaymentsCoreAsync(
        DateTimeOffset expiresBefore,
        TimeSpan observedConfirmationWait,
        int maxPayments,
        CancellationToken cancellationToken)
    {
        await using var transaction = await BeginTransactionIfRelationalAsync(cancellationToken);

        var confirmationWaitCutoff = expiresBefore - observedConfirmationWait;
        var observedInTime = PaymentsWithTransactionObservedInTime();
        var duePayments = await dbContext.Payments
            .Where(payment =>
                (payment.Status == "pending_currency_selection" && payment.ExpiresAt <= expiresBefore) ||
                (payment.Status == "waiting_for_payment" && payment.LateAcceptanceEndsAt <= expiresBefore) ||
                (payment.Status == "observed" &&
                 payment.LateAcceptanceEndsAt <= expiresBefore &&
                 (payment.LateAcceptanceEndsAt <= confirmationWaitCutoff || !observedInTime.Contains(payment.Id))))
            .OrderBy(payment => payment.LateAcceptanceEndsAt)
            .Take(maxPayments)
            .ToListAsync(cancellationToken);

        foreach (var payment in duePayments)
        {
            payment.Status = "expired";
            payment.UpdatedAt = expiresBefore;
            payment.Version++;
            dbContext.PaymentEventHistory.Add(new PaymentEventHistoryRecord
            {
                ProjectId = payment.ProjectId,
                Id = Guid.NewGuid(),
                PaymentId = payment.Id,
                EventType = "payment.expired",
                OccurredAt = expiresBefore,
                Details = null,
            });
            dbContext.WebhookOutboxEvents.Add(CreateWebhookOutboxEvent(
                payment,
                "payment.expired",
                expiresBefore));
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        return new ExpireDuePaymentsStoreResult(duePayments.Count);
    }

    public Task<UpdateBlockchainTransactionConfirmationsStoreResult> UpdateBlockchainTransactionConfirmationsAsync(
        BlockchainTransactionConfirmationUpdateDraft confirmationUpdate,
        PaymentCompletionPolicyDraft completionPolicy,
        PaymentEventDraft completedPaymentEvent,
        WebhookOutboxEventDraft completedWebhookEvent,
        PaymentEventDraft reorgPaymentEvent,
        CancellationToken cancellationToken) =>
        DiscardChangesOnFailureAsync(() => UpdateBlockchainTransactionConfirmationsCoreAsync(
            confirmationUpdate,
            completionPolicy,
            completedPaymentEvent,
            completedWebhookEvent,
            reorgPaymentEvent,
            cancellationToken));

    private async Task<UpdateBlockchainTransactionConfirmationsStoreResult> UpdateBlockchainTransactionConfirmationsCoreAsync(
        BlockchainTransactionConfirmationUpdateDraft confirmationUpdate,
        PaymentCompletionPolicyDraft completionPolicy,
        PaymentEventDraft completedPaymentEvent,
        WebhookOutboxEventDraft completedWebhookEvent,
        PaymentEventDraft reorgPaymentEvent,
        CancellationToken cancellationToken)
    {
        await using var transaction = await BeginTransactionIfRelationalAsync(cancellationToken);

        var payment = await dbContext.Payments
            .SingleOrDefaultAsync(
                candidate => candidate.Id == confirmationUpdate.PaymentId &&
                             candidate.ProjectId == confirmationUpdate.ProjectId,
                cancellationToken);
        if (payment is null)
        {
            return UpdateBlockchainTransactionConfirmationsStoreResult.PaymentNotFound();
        }

        if (payment.Status is not ("waiting_for_payment" or "observed" or "completed"))
        {
            return UpdateBlockchainTransactionConfirmationsStoreResult.PaymentNotReady();
        }

        var matchingTransaction = await dbContext.MatchingBlockchainTransactions
            .SingleOrDefaultAsync(
                candidate => candidate.ProjectId == payment.ProjectId &&
                             candidate.PaymentId == confirmationUpdate.PaymentId &&
                             candidate.SupportedCurrency == confirmationUpdate.SupportedCurrency &&
                             candidate.TransactionHash == confirmationUpdate.TransactionHash,
                cancellationToken);
        if (matchingTransaction is null)
        {
            return UpdateBlockchainTransactionConfirmationsStoreResult.TransactionNotFound();
        }

        var previousConfirmations = matchingTransaction.Confirmations;
        var previousBlockHash = matchingTransaction.BlockHash;
        var previousBlockHeight = matchingTransaction.BlockHeight;
        completionPolicy = ResolveCompletionPolicy(payment, completionPolicy);
        var monitored = IsMonitoredAfterCompletion(payment, matchingTransaction);
        var blockMoved = monitored &&
                         (IsChanged(previousBlockHash, confirmationUpdate.BlockHash) ||
                          IsChanged(previousBlockHeight, confirmationUpdate.BlockHeight));
        var confirmationsDropped = monitored && confirmationUpdate.Confirmations < previousConfirmations;
        matchingTransaction.ConsecutiveMissingCount = 0;
        matchingTransaction.ConsecutiveConfirmationDropCount =
            confirmationsDropped ? matchingTransaction.ConsecutiveConfirmationDropCount + 1 : 0;
        var reorgSignal = blockMoved ||
                          matchingTransaction.ConsecutiveConfirmationDropCount >= RequiredConsecutiveConfirmationDrops;

        // A single dip is taken for a provider that lags a block: the stored
        // depth stays until the drop is reported again.
        if (!confirmationsDropped || reorgSignal)
        {
            matchingTransaction.Confirmations = confirmationUpdate.Confirmations;
        }

        matchingTransaction.BlockHash = confirmationUpdate.BlockHash ?? matchingTransaction.BlockHash;
        matchingTransaction.BlockHeight = confirmationUpdate.BlockHeight ?? matchingTransaction.BlockHeight;
        matchingTransaction.LastCheckedAt = confirmationUpdate.CheckedAt;
        matchingTransaction.UpdatedAt = confirmationUpdate.CheckedAt;
        matchingTransaction.Version++;

        var reorgDetected = reorgSignal && await RaiseReorgAlertAsync(
            payment,
            matchingTransaction,
            previousConfirmations,
            confirmationUpdate.Confirmations,
            previousBlockHash,
            confirmationUpdate.BlockHash,
            previousBlockHeight,
            confirmationUpdate.BlockHeight,
            confirmationUpdate.CheckedAt,
            reorgPaymentEvent,
            cancellationToken);
        if (reorgSignal)
        {
            matchingTransaction.ConsecutiveConfirmationDropCount = 0;
        }

        var completion = await CalculateCompletionAsync(payment, matchingTransaction, completionPolicy, cancellationToken);
        if (completion.IsCompleted)
        {
            await ApplyCompletionAsync(
                payment,
                matchingTransaction,
                completion,
                completedPaymentEvent,
                completedWebhookEvent,
                confirmationUpdate.CheckedAt,
                cancellationToken);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        var observedTotal = await CalculateObservedTotalAsync(payment.ProjectId, payment.Id, cancellationToken);

        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        if (reorgDetected)
        {
            return UpdateBlockchainTransactionConfirmationsStoreResult.ReorgAlerted(ToReadModel(payment, observedTotal));
        }

        return StringComparer.Ordinal.Equals(payment.Status, "completed")
            ? UpdateBlockchainTransactionConfirmationsStoreResult.Completed(ToReadModel(payment, observedTotal))
            : UpdateBlockchainTransactionConfirmationsStoreResult.Updated(ToReadModel(payment, observedTotal));
    }

    public Task<UpdateBlockchainTransactionConfirmationsStoreResult> RecordMissingBlockchainTransactionAsync(
        BlockchainTransactionMissingDraft missingTransaction,
        PaymentEventDraft reorgPaymentEvent,
        CancellationToken cancellationToken) =>
        DiscardChangesOnFailureAsync(() => RecordMissingBlockchainTransactionCoreAsync(
            missingTransaction,
            reorgPaymentEvent,
            cancellationToken));

    /// <summary>
    /// The provider no longer reports a transaction that completed a Payment,
    /// which is what a reorganisation or a double spend looks like once it has
    /// happened. It raises a Reorg Alert after consecutive misses.
    /// </summary>
    private async Task<UpdateBlockchainTransactionConfirmationsStoreResult> RecordMissingBlockchainTransactionCoreAsync(
        BlockchainTransactionMissingDraft missingTransaction,
        PaymentEventDraft reorgPaymentEvent,
        CancellationToken cancellationToken)
    {
        await using var transaction = await BeginTransactionIfRelationalAsync(cancellationToken);

        var payment = await dbContext.Payments
            .SingleOrDefaultAsync(
                candidate => candidate.Id == missingTransaction.PaymentId &&
                             candidate.ProjectId == missingTransaction.ProjectId,
                cancellationToken);
        if (payment is null)
        {
            return UpdateBlockchainTransactionConfirmationsStoreResult.PaymentNotFound();
        }

        var matchingTransaction = await dbContext.MatchingBlockchainTransactions
            .SingleOrDefaultAsync(
                candidate => candidate.ProjectId == payment.ProjectId &&
                             candidate.PaymentId == payment.Id &&
                             candidate.SupportedCurrency == missingTransaction.SupportedCurrency &&
                             candidate.TransactionHash == missingTransaction.TransactionHash,
                cancellationToken);
        if (matchingTransaction is null)
        {
            return UpdateBlockchainTransactionConfirmationsStoreResult.TransactionNotFound();
        }

        if (!IsMonitoredAfterCompletion(payment, matchingTransaction))
        {
            return UpdateBlockchainTransactionConfirmationsStoreResult.PaymentNotReady();
        }

        matchingTransaction.ConsecutiveMissingCount++;
        matchingTransaction.LastCheckedAt = missingTransaction.CheckedAt;
        matchingTransaction.UpdatedAt = missingTransaction.CheckedAt;
        matchingTransaction.Version++;
        var reorgDetected = matchingTransaction.ConsecutiveMissingCount >= RequiredConsecutiveMisses &&
                            await RaiseReorgAlertAsync(
                                payment,
                                matchingTransaction,
                                matchingTransaction.Confirmations,
                                newConfirmations: 0,
                                matchingTransaction.BlockHash,
                                newBlockHash: null,
                                matchingTransaction.BlockHeight,
                                newBlockHeight: null,
                                missingTransaction.CheckedAt,
                                reorgPaymentEvent,
                                cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);
        var observedTotal = await CalculateObservedTotalAsync(payment.ProjectId, payment.Id, cancellationToken);

        if (transaction is not null)
        {
            await transaction.CommitAsync(cancellationToken);
        }

        return reorgDetected
            ? UpdateBlockchainTransactionConfirmationsStoreResult.ReorgAlerted(ToReadModel(payment, observedTotal))
            : UpdateBlockchainTransactionConfirmationsStoreResult.Updated(ToReadModel(payment, observedTotal));
    }

    /// <summary>
    /// Payments with a Matching Blockchain Transaction whose Observed Payment
    /// Time lies inside Payment Expiration or the Late Acceptance Window, which
    /// is what lets them complete once it is confirmed.
    /// </summary>
    private IQueryable<Guid> PaymentsWithTransactionObservedInTime() =>
        from transaction in dbContext.MatchingBlockchainTransactions
        join payment in dbContext.Payments
            on new { transaction.ProjectId, Id = transaction.PaymentId }
            equals new { payment.ProjectId, payment.Id }
        where transaction.SupportedCurrency == payment.SelectedCurrency &&
              transaction.PaymentAddress == payment.PaymentAddress &&
              transaction.ObservedAt <= payment.LateAcceptanceEndsAt
        select payment.Id;

    /// <summary>
    /// A write that fails leaves its entities in the change tracker, and the
    /// next save in the same scope would send them again. A worker run shares
    /// one scope across many Payments, so without this one failed write would
    /// fail every later write of the run, including the lease release.
    /// </summary>
    private async Task<T> DiscardChangesOnFailureAsync<T>(Func<Task<T>> write)
    {
        try
        {
            return await write();
        }
        catch
        {
            dbContext.ChangeTracker.Clear();
            throw;
        }
    }

    private async Task<IDbContextTransaction?> BeginTransactionIfRelationalAsync(CancellationToken cancellationToken)
    {
        return dbContext.Database.IsRelational()
            ? await dbContext.Database.BeginTransactionAsync(cancellationToken)
            : null;
    }

    private static PaymentRecord ToRecord(Guid projectId, PaymentDraft payment)
    {
        return new PaymentRecord
        {
            ProjectId = projectId,
            Id = payment.Id,
            IntegrationApiCredentialId = payment.IntegrationApiCredentialId,
            ExternalReference = payment.ExternalReference,
            FiatCurrency = payment.FiatCurrency,
            FiatAmountMinor = payment.FiatAmountMinor,
            Status = payment.Status,
            PayerPageId = payment.PayerPageId,
            ExpiresAt = payment.ExpiresAt,
            LateAcceptanceEndsAt = payment.LateAcceptanceEndsAt,
            ContextUsername = payment.ContextUsername,
            ContextCustomerNumber = payment.ContextCustomerNumber,
            ContextCartName = payment.ContextCartName,
            ContextNote = payment.ContextNote,
            ReturnUrl = payment.ReturnUrl,
            CreatedAt = payment.CreatedAt,
            UpdatedAt = payment.UpdatedAt,
        };
    }

    private static PaymentOptionRecord ToRecord(Guid projectId, PaymentOptionDraft option)
    {
        return new PaymentOptionRecord
        {
            ProjectId = projectId,
            PaymentId = option.PaymentId,
            SupportedCurrency = option.SupportedCurrency,
            Status = option.Status,
            UnavailableReason = option.UnavailableReason,
            CreatedAt = option.CreatedAt,
        };
    }

    private static PaymentEventHistoryRecord ToRecord(Guid projectId, PaymentEventDraft paymentEvent)
    {
        return new PaymentEventHistoryRecord
        {
            ProjectId = projectId,
            Id = paymentEvent.Id,
            PaymentId = paymentEvent.PaymentId,
            EventType = paymentEvent.EventType,
            OccurredAt = paymentEvent.OccurredAt,
            Details = paymentEvent.Details,
        };
    }

    private static WebhookOutboxEventRecord ToRecord(Guid projectId, WebhookOutboxEventDraft webhookEvent)
    {
        return new WebhookOutboxEventRecord
        {
            ProjectId = projectId,
            Id = webhookEvent.Id,
            PaymentId = webhookEvent.PaymentId,
            IntegrationApiCredentialId = webhookEvent.IntegrationApiCredentialId,
            EventType = webhookEvent.EventType,
            EventVersion = webhookEvent.EventVersion,
            PayloadVersion = webhookEvent.PayloadVersion,
            ResourceType = webhookEvent.ResourceType,
            ResourceId = webhookEvent.ResourceId,
            Status = webhookEvent.Status,
            OccurredAt = webhookEvent.OccurredAt,
            CreatedAt = webhookEvent.CreatedAt,
            NextAttemptAt = webhookEvent.NextAttemptAt,
            AttemptCount = webhookEvent.AttemptCount,
            CorrelationId = webhookEvent.CorrelationId,
        };
    }

    private static WebhookOutboxEventRecord CreateWebhookOutboxEvent(
        PaymentRecord payment,
        string eventType,
        DateTimeOffset occurredAt)
    {
        var eventId = Guid.NewGuid();
        return new WebhookOutboxEventRecord
        {
            ProjectId = payment.ProjectId,
            Id = eventId,
            PaymentId = payment.Id,
            IntegrationApiCredentialId = payment.IntegrationApiCredentialId,
            EventType = eventType,
            EventVersion = "1",
            PayloadVersion = 1,
            ResourceType = "payment",
            ResourceId = payment.Id.ToString("D"),
            Status = "pending",
            OccurredAt = occurredAt,
            CreatedAt = occurredAt,
            NextAttemptAt = occurredAt,
            AttemptCount = 0,
            CorrelationId = eventId.ToString("D"),
        };
    }

    private static MatchingBlockchainTransactionRecord ToRecord(
        Guid projectId,
        BlockchainObservationDraft observation)
    {
        return new MatchingBlockchainTransactionRecord
        {
            ProjectId = projectId,
            Id = observation.Id,
            PaymentId = observation.PaymentId,
            SupportedCurrency = observation.SupportedCurrency,
            PaymentAddress = observation.PaymentAddress,
            TransactionHash = observation.TransactionHash,
            ObservedAmount = observation.ObservedAmount,
            ObservedAt = observation.ObservedAt,
            FirstObservedAt = observation.FirstObservedAt,
            Confirmations = observation.Confirmations,
            ProviderName = observation.ProviderName,
            ProviderObservationId = observation.ProviderObservationId,
            LastCheckedAt = observation.UpdatedAt,
            CreatedAt = observation.CreatedAt,
            UpdatedAt = observation.UpdatedAt,
        };
    }

    private async Task<string?> CalculateObservedTotalAsync(
        Guid projectId,
        Guid paymentId,
        CancellationToken cancellationToken)
    {
        var amounts = await dbContext.MatchingBlockchainTransactions
            .AsNoTracking()
            .Where(transaction => transaction.ProjectId == projectId && transaction.PaymentId == paymentId)
            .Select(transaction => transaction.ObservedAmount)
            .ToListAsync(cancellationToken);
        if (amounts.Count == 0)
        {
            return null;
        }

        var total = amounts.Sum(amount => decimal.Parse(amount, CultureInfo.InvariantCulture));
        return total.ToString("0.############################", CultureInfo.InvariantCulture);
    }

    private async Task<CompletionCalculation> CalculateCompletionAsync(
        PaymentRecord payment,
        MatchingBlockchainTransactionRecord candidateTransaction,
        PaymentCompletionPolicyDraft completionPolicy,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(payment.ExpectedCryptoAmount))
        {
            return CompletionCalculation.NotCompleted();
        }

        var existingEligibleTransactions = await dbContext.MatchingBlockchainTransactions
            .AsNoTracking()
            .Where(transaction =>
                transaction.ProjectId == payment.ProjectId &&
                transaction.PaymentId == payment.Id &&
                transaction.Id != candidateTransaction.Id &&
                transaction.SupportedCurrency == payment.SelectedCurrency &&
                transaction.PaymentAddress == payment.PaymentAddress &&
                transaction.Confirmations >= completionPolicy.RequiredConfirmations &&
                transaction.ObservedAt <= payment.LateAcceptanceEndsAt)
            .Select(transaction => new
            {
                transaction.Id,
                transaction.ObservedAmount,
            })
            .ToListAsync(cancellationToken);

        var eligibleTotal = existingEligibleTransactions
            .Sum(transaction => decimal.Parse(transaction.ObservedAmount, CultureInfo.InvariantCulture));
        var candidateTransactionContributed =
            candidateTransaction.Confirmations >= completionPolicy.RequiredConfirmations &&
            candidateTransaction.ObservedAt <= payment.LateAcceptanceEndsAt;
        if (candidateTransactionContributed)
        {
            eligibleTotal += decimal.Parse(candidateTransaction.ObservedAmount, CultureInfo.InvariantCulture);
        }

        var expectedAmount = decimal.Parse(payment.ExpectedCryptoAmount, CultureInfo.InvariantCulture);
        var toleranceMultiplier = Math.Max(0m, 100m - completionPolicy.PaymentTolerancePercent) / 100m;
        var minimumAcceptedAmount = expectedAmount * toleranceMultiplier;
        if (eligibleTotal < minimumAcceptedAmount)
        {
            return CompletionCalculation.NotCompleted();
        }

        return new CompletionCalculation(
            IsCompleted: true,
            FormatCryptoAmount(eligibleTotal),
            existingEligibleTransactions.Select(transaction => transaction.Id).ToArray(),
            candidateTransactionContributed);
    }

    /// <summary>
    /// How far before currency selection a transaction's Observed Payment
    /// Time may lie and still count. Block timestamps may trail real time by
    /// well over an hour.
    /// </summary>
    public static readonly TimeSpan PreSelectionTolerance = TimeSpan.FromHours(2);

    /// <summary>
    /// Records a transaction the Payment Address received before currency
    /// selection as an Address History Alert, once, without touching the
    /// Payment. It never counts toward the Payment.
    /// </summary>
    private async Task<RecordBlockchainObservationStoreResult> RecordAddressHistoryAsync(
        PaymentRecord payment,
        BlockchainObservationDraft observation,
        DateTimeOffset currencySelectedAt,
        CancellationToken cancellationToken)
    {
        var alreadyRecorded = await dbContext.AddressHistoryAlerts
            .AsNoTracking()
            .AnyAsync(
                alert => alert.ProjectId == payment.ProjectId &&
                         alert.PaymentId == payment.Id &&
                         alert.SupportedCurrency == observation.SupportedCurrency &&
                         alert.TransactionHash == observation.TransactionHash,
                cancellationToken);
        if (!alreadyRecorded)
        {
            dbContext.AddressHistoryAlerts.Add(new AddressHistoryAlertRecord
            {
                ProjectId = payment.ProjectId,
                Id = Guid.NewGuid(),
                PaymentId = payment.Id,
                SupportedCurrency = observation.SupportedCurrency,
                PaymentAddress = observation.PaymentAddress,
                TransactionHash = observation.TransactionHash,
                ObservedAmount = observation.ObservedAmount,
                ObservedAt = observation.ObservedAt,
                CurrencySelectedAt = currencySelectedAt,
                Status = "open",
                CreatedAt = observation.CreatedAt,
                UpdatedAt = observation.CreatedAt,
            });
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        var readModel = ToReadModel(
            payment,
            await CalculateObservedTotalAsync(payment.ProjectId, payment.Id, cancellationToken));
        return alreadyRecorded
            ? RecordBlockchainObservationStoreResult.AlreadyIgnoredBeforeSelection(readModel)
            : RecordBlockchainObservationStoreResult.IgnoredBeforeSelection(readModel);
    }

    /// <summary>
    /// Consecutive checks a contributing transaction may be missing from the
    /// provider before it is taken for reorganised out or double-spent. One
    /// miss is more often a provider that lags or truncates an address.
    /// </summary>
    private const int RequiredConsecutiveMisses = 3;

    /// <summary>
    /// Consecutive checks that must report fewer confirmations than stored
    /// before a drop is taken for a reorganisation rather than provider lag.
    /// </summary>
    private const int RequiredConsecutiveConfirmationDrops = 2;

    private static bool IsMonitoredAfterCompletion(
        PaymentRecord payment,
        MatchingBlockchainTransactionRecord matchingTransaction) =>
        StringComparer.Ordinal.Equals(payment.Status, "completed") &&
        matchingTransaction.ContributedToCompletion;

    /// <summary>
    /// Only a value the provider reported both times can have changed; an
    /// unknown block is not a different block.
    /// </summary>
    private static bool IsChanged(string? previous, string? current) =>
        !string.IsNullOrWhiteSpace(previous) &&
        !string.IsNullOrWhiteSpace(current) &&
        !StringComparer.Ordinal.Equals(previous, current);

    private static bool IsChanged(long? previous, long? current) =>
        previous.HasValue && current.HasValue && previous.Value != current.Value;

    /// <summary>
    /// Raises a Reorg Alert unless one is already open for the transaction,
    /// so a signal that keeps being reported produces one alert for the Admin
    /// to review. Returns whether an alert was raised.
    /// </summary>
    private async Task<bool> RaiseReorgAlertAsync(
        PaymentRecord payment,
        MatchingBlockchainTransactionRecord matchingTransaction,
        int previousConfirmations,
        int newConfirmations,
        string? previousBlockHash,
        string? newBlockHash,
        long? previousBlockHeight,
        long? newBlockHeight,
        DateTimeOffset checkedAt,
        PaymentEventDraft reorgPaymentEvent,
        CancellationToken cancellationToken)
    {
        matchingTransaction.ReorgAffected = true;
        if (await dbContext.ReorgAlerts.AnyAsync(
                alert => alert.ProjectId == payment.ProjectId &&
                         alert.MatchingBlockchainTransactionId == matchingTransaction.Id &&
                         alert.Status == "open",
                cancellationToken))
        {
            return false;
        }

        dbContext.ReorgAlerts.Add(new ReorgAlertRecord
        {
            ProjectId = payment.ProjectId,
            Id = Guid.NewGuid(),
            PaymentId = payment.Id,
            MatchingBlockchainTransactionId = matchingTransaction.Id,
            SupportedCurrency = matchingTransaction.SupportedCurrency,
            TransactionHash = matchingTransaction.TransactionHash,
            PreviousConfirmations = previousConfirmations,
            NewConfirmations = newConfirmations,
            PreviousBlockHash = previousBlockHash,
            NewBlockHash = newBlockHash,
            PreviousBlockHeight = previousBlockHeight,
            NewBlockHeight = newBlockHeight,
            Status = "open",
            CreatedAt = checkedAt,
            UpdatedAt = checkedAt,
        });
        dbContext.PaymentEventHistory.Add(ToRecord(payment.ProjectId, reorgPaymentEvent));
        return true;
    }

    private async Task ApplyCompletionAsync(
        PaymentRecord payment,
        MatchingBlockchainTransactionRecord candidateTransaction,
        CompletionCalculation completion,
        PaymentEventDraft completedPaymentEvent,
        WebhookOutboxEventDraft completedWebhookEvent,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken)
    {
        if (StringComparer.Ordinal.Equals(payment.Status, "completed"))
        {
            return;
        }

        payment.Status = "completed";
        payment.ConfirmedEligibleTotal = completion.ConfirmedEligibleTotal;
        payment.CompletedAt = completedAt;
        payment.UpdatedAt = completedAt;
        payment.Version++;
        candidateTransaction.ContributedToCompletion = completion.CandidateTransactionContributed;
        foreach (var transactionId in completion.ExistingContributingTransactionIds)
        {
            await dbContext.MatchingBlockchainTransactions
                .Where(transaction =>
                    transaction.ProjectId == payment.ProjectId &&
                    transaction.Id == transactionId)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(transaction => transaction.ContributedToCompletion, true)
                        .SetProperty(transaction => transaction.UpdatedAt, completedAt),
                    cancellationToken);
        }

        dbContext.PaymentEventHistory.Add(ToRecord(payment.ProjectId, completedPaymentEvent));
        dbContext.WebhookOutboxEvents.Add(ToRecord(payment.ProjectId, completedWebhookEvent with
        {
            IntegrationApiCredentialId = payment.IntegrationApiCredentialId,
        }));
    }

    private static string FormatCryptoAmount(decimal amount)
    {
        return amount.ToString("0.############################", CultureInfo.InvariantCulture);
    }

    private async Task<IReadOnlyList<PaymentOptionReadModel>> LoadPaymentOptionsAsync(
        Guid projectId,
        Guid paymentId,
        CancellationToken cancellationToken)
    {
        return await dbContext.PaymentOptions
            .AsNoTracking()
            .Where(option => option.ProjectId == projectId && option.PaymentId == paymentId)
            .OrderBy(option => option.SupportedCurrency)
            .Select(option => new PaymentOptionReadModel(
                option.SupportedCurrency,
                option.Status,
                option.UnavailableReason,
                option.CreatedAt))
            .ToListAsync(cancellationToken);
    }

    private async Task<RateLockReadModel?> LoadRateLockAsync(
        Guid projectId,
        Guid paymentId,
        CancellationToken cancellationToken)
    {
        var rateLock = await dbContext.RateLocks
            .AsNoTracking()
            .SingleOrDefaultAsync(
                candidate => candidate.ProjectId == projectId && candidate.PaymentId == paymentId,
                cancellationToken);
        return rateLock is null ? null : ToReadModel(rateLock);
    }

    private async Task<PaymentInstructionReadModel?> LoadPaymentInstructionAsync(
        Guid projectId,
        Guid paymentId,
        CancellationToken cancellationToken)
    {
        return await (
                from assignment in dbContext.PaymentAddressAssignments.AsNoTracking()
                join payment in dbContext.Payments.AsNoTracking()
                    on new { assignment.ProjectId, assignment.PaymentId }
                    equals new { payment.ProjectId, PaymentId = payment.Id }
                where assignment.ProjectId == projectId && assignment.PaymentId == paymentId
                select new PaymentInstructionReadModel(
                    assignment.SupportedCurrency,
                    assignment.Network,
                    assignment.ChainId,
                    payment.ExpectedCryptoAmount!,
                    assignment.PaymentAddress))
            .SingleOrDefaultAsync(cancellationToken);
    }

    private static RateLockReadModel ToReadModel(RateLockRecord rateLock) =>
        new(
            rateLock.FiatCurrency,
            rateLock.FiatAmountMinor,
            rateLock.SupportedCurrency,
            rateLock.ExpectedCryptoAmount,
            rateLock.RateValue,
            rateLock.RateSource,
            rateLock.RateObservedAt,
            rateLock.CreatedAt);

    private static PaymentReadModel ToReadModel(
        PaymentRecord payment,
        string? observedTotal = null,
        IReadOnlyList<PaymentOptionReadModel>? paymentOptions = null,
        RateLockReadModel? rateLock = null,
        PaymentInstructionReadModel? paymentInstruction = null)
    {
        return new PaymentReadModel(
            payment.Id,
            payment.IntegrationApiCredentialId,
            payment.ExternalReference,
            payment.FiatCurrency,
            payment.FiatAmountMinor,
            payment.Status,
            payment.PayerPageId,
            payment.ExpiresAt,
            payment.LateAcceptanceEndsAt,
            payment.ContextUsername,
            payment.ContextCustomerNumber,
            payment.ContextCartName,
            payment.ContextNote,
            payment.ReturnUrl,
            payment.SelectedCurrency,
            payment.ExpectedCryptoAmount,
            payment.PaymentAddress,
            observedTotal,
            payment.ConfirmedEligibleTotal,
            payment.CompletedAt,
            payment.CreatedAt,
            payment.UpdatedAt,
            paymentOptions,
            payment.SettledAt,
            payment.ProjectId,
            rateLock,
            paymentInstruction);
    }

    private static PaymentReadModel ToReadModel(
        Guid projectId,
        PaymentDraft payment,
        IReadOnlyCollection<PaymentOptionDraft> paymentOptions)
    {
        return new PaymentReadModel(
            payment.Id,
            payment.IntegrationApiCredentialId,
            payment.ExternalReference,
            payment.FiatCurrency,
            payment.FiatAmountMinor,
            payment.Status,
            payment.PayerPageId,
            payment.ExpiresAt,
            payment.LateAcceptanceEndsAt,
            payment.ContextUsername,
            payment.ContextCustomerNumber,
            payment.ContextCartName,
            payment.ContextNote,
            payment.ReturnUrl,
            SelectedCurrency: null,
            ExpectedCryptoAmount: null,
            PaymentAddress: null,
            ObservedTotal: null,
            ConfirmedEligibleTotal: null,
            CompletedAt: null,
            payment.CreatedAt,
            payment.UpdatedAt,
            paymentOptions
                .OrderBy(option => option.SupportedCurrency)
                .Select(option => new PaymentOptionReadModel(
                    option.SupportedCurrency,
                    option.Status,
                    option.UnavailableReason,
                    option.CreatedAt))
                .ToArray(),
            SettledAt: null,
            ProjectId: projectId);
    }

    private static PaymentCompletionPolicyDraft ResolveCompletionPolicy(
        PaymentRecord payment,
        PaymentCompletionPolicyDraft fallback) =>
        new(
            payment.ConfirmationRequirement ?? fallback.RequiredConfirmations,
            payment.PaymentTolerancePercent ?? fallback.PaymentTolerancePercent);

    private sealed record CompletionCalculation(
        bool IsCompleted,
        string? ConfirmedEligibleTotal,
        IReadOnlyCollection<Guid> ExistingContributingTransactionIds,
        bool CandidateTransactionContributed)
    {
        public static CompletionCalculation NotCompleted() =>
            new(
                IsCompleted: false,
                ConfirmedEligibleTotal: null,
                ExistingContributingTransactionIds: [],
                CandidateTransactionContributed: false);
    }
}
