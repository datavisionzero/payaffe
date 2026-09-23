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
        int maxPayments,
        CancellationToken cancellationToken)
    {
        return await dbContext.Payments
            .AsNoTracking()
            .Where(payment =>
                (payment.Status == "waiting_for_payment" || payment.Status == "observed") &&
                payment.LateAcceptanceEndsAt > observedUntil &&
                payment.SelectedCurrency != null &&
                payment.PaymentAddress != null &&
                payment.ExpectedCryptoAmount != null)
            .OrderBy(payment => payment.UpdatedAt)
            .ThenBy(payment => payment.Id)
            .Take(maxPayments)
            .Select(payment => new BlockchainObservationTarget(
                payment.Id,
                payment.SelectedCurrency!,
                payment.PaymentAddress!,
                payment.ExpectedCryptoAmount!,
                payment.ProjectId))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<BlockchainReorgMonitoringTarget>> ListBlockchainReorgMonitoringTargetsAsync(
        ReorgMonitoringPolicyDraft monitoringPolicy,
        int maxTransactions,
        CancellationToken cancellationToken)
    {
        return await (
                from transaction in dbContext.MatchingBlockchainTransactions.AsNoTracking()
                join payment in dbContext.Payments.AsNoTracking()
                    on new { transaction.ProjectId, Id = transaction.PaymentId }
                    equals new { payment.ProjectId, payment.Id }
                where payment.Status == "completed" &&
                      transaction.ContributedToCompletion &&
                      !transaction.ReorgAffected &&
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
                select new BlockchainReorgMonitoringTarget(
                    payment.Id,
                    transaction.SupportedCurrency,
                    transaction.PaymentAddress,
                    payment.ExpectedCryptoAmount!,
                    transaction.TransactionHash,
                    transaction.Confirmations,
                    payment.ProjectId))
            .Take(maxTransactions)
            .ToListAsync(cancellationToken);
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
            .SingleOrDefaultAsync(candidate => candidate.Id == selection.PaymentId, cancellationToken);
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
                             (observation.ProjectId == Guid.Empty || candidate.ProjectId == observation.ProjectId),
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

        var existingTransaction = await dbContext.MatchingBlockchainTransactions
            .AsNoTracking()
            .SingleOrDefaultAsync(
                candidate => candidate.SupportedCurrency == observation.SupportedCurrency &&
                             candidate.TransactionHash == observation.TransactionHash &&
                             candidate.ProjectId == payment.ProjectId,
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
        int maxPayments,
        CancellationToken cancellationToken) =>
        DiscardChangesOnFailureAsync(() => ExpireDuePaymentsCoreAsync(
            expiresBefore, maxPayments, cancellationToken));

    private async Task<ExpireDuePaymentsStoreResult> ExpireDuePaymentsCoreAsync(
        DateTimeOffset expiresBefore,
        int maxPayments,
        CancellationToken cancellationToken)
    {
        await using var transaction = await BeginTransactionIfRelationalAsync(cancellationToken);

        var duePayments = await dbContext.Payments
            .Where(payment =>
                (payment.Status == "pending_currency_selection" ||
                 payment.Status == "waiting_for_payment" ||
                 payment.Status == "observed") &&
                payment.LateAcceptanceEndsAt <= expiresBefore)
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
                             (confirmationUpdate.ProjectId == Guid.Empty || candidate.ProjectId == confirmationUpdate.ProjectId),
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
        var reorgDetected = IsReorgAffectedCompletedTransaction(
            payment,
            matchingTransaction,
            completionPolicy,
            confirmationUpdate,
            previousConfirmations,
            previousBlockHash,
            previousBlockHeight);

        matchingTransaction.Confirmations = confirmationUpdate.Confirmations;
        matchingTransaction.BlockHash = confirmationUpdate.BlockHash;
        matchingTransaction.BlockHeight = confirmationUpdate.BlockHeight;
        matchingTransaction.LastCheckedAt = confirmationUpdate.CheckedAt;
        matchingTransaction.UpdatedAt = confirmationUpdate.CheckedAt;
        matchingTransaction.Version++;

        if (reorgDetected)
        {
            matchingTransaction.ReorgAffected = true;
            dbContext.ReorgAlerts.Add(new ReorgAlertRecord
            {
                ProjectId = payment.ProjectId,
                Id = Guid.NewGuid(),
                PaymentId = payment.Id,
                MatchingBlockchainTransactionId = matchingTransaction.Id,
                SupportedCurrency = matchingTransaction.SupportedCurrency,
                TransactionHash = matchingTransaction.TransactionHash,
                PreviousConfirmations = previousConfirmations,
                NewConfirmations = confirmationUpdate.Confirmations,
                PreviousBlockHash = previousBlockHash,
                NewBlockHash = confirmationUpdate.BlockHash,
                PreviousBlockHeight = previousBlockHeight,
                NewBlockHeight = confirmationUpdate.BlockHeight,
                Status = "open",
                CreatedAt = confirmationUpdate.CheckedAt,
                UpdatedAt = confirmationUpdate.CheckedAt,
            });
            dbContext.PaymentEventHistory.Add(ToRecord(payment.ProjectId, reorgPaymentEvent));
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

    private static bool IsReorgAffectedCompletedTransaction(
        PaymentRecord payment,
        MatchingBlockchainTransactionRecord matchingTransaction,
        PaymentCompletionPolicyDraft completionPolicy,
        BlockchainTransactionConfirmationUpdateDraft confirmationUpdate,
        int previousConfirmations,
        string? previousBlockHash,
        long? previousBlockHeight)
    {
        if (!StringComparer.Ordinal.Equals(payment.Status, "completed") ||
            !matchingTransaction.ContributedToCompletion ||
            matchingTransaction.ReorgAffected)
        {
            return false;
        }

        var blockHashChanged =
            !string.IsNullOrWhiteSpace(previousBlockHash) &&
            !string.IsNullOrWhiteSpace(confirmationUpdate.BlockHash) &&
            !StringComparer.Ordinal.Equals(previousBlockHash, confirmationUpdate.BlockHash);
        var blockHeightChanged =
            previousBlockHeight.HasValue &&
            confirmationUpdate.BlockHeight.HasValue &&
            previousBlockHeight.Value != confirmationUpdate.BlockHeight.Value;
        var confirmationsDroppedBelowRequired =
            previousConfirmations >= completionPolicy.RequiredConfirmations &&
            confirmationUpdate.Confirmations < completionPolicy.RequiredConfirmations;
        var confirmationsDecreased = confirmationUpdate.Confirmations < previousConfirmations;

        return blockHashChanged ||
               blockHeightChanged ||
               confirmationsDroppedBelowRequired ||
               confirmationsDecreased;
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
