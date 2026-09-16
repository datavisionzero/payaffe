using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Globalization;
using Payaffe.Domain.Payments;
using Microsoft.Extensions.Options;

namespace Payaffe.Application.Payments;

public sealed class PaymentApplicationService(
    IPaymentStore paymentStore,
    IPayerPageIdGenerator payerPageIdGenerator,
    IExchangeRateSource exchangeRateSource,
    IPaymentAddressProvider paymentAddressProvider,
    IBlockchainObservationAdapter blockchainObservationAdapter,
    IClock clock,
    IOptions<PaymentApplicationOptions> options)
{
    private static readonly string[] SupportedCurrencies = ["BTC", "LTC", "ETH"];

    private readonly PaymentApplicationOptions _options = options.Value;

    public async Task<CreatePaymentResult> CreateAsync(
        Guid integrationApiCredentialId,
        CreatePaymentCommand command,
        CancellationToken cancellationToken)
    {
        var idempotencyKey = NormalizeIdempotencyKey(command.IdempotencyKey);
        var externalReference = ExternalReference.Create(command.ExternalReference);
        var fiatAmount = FiatAmount.Create(command.FiatCurrency, command.FiatAmountMinor);
        var context = PaymentContextFields.Create(
            command.PaymentContext?.Username,
            command.PaymentContext?.CustomerNumber,
            command.PaymentContext?.CartName,
            command.PaymentContext?.Note);
        var returnUrl = NormalizeReturnUrl(command.ReturnUrl);
        var createdAt = clock.UtcNow;

        var payment = Payment.Create(
            integrationApiCredentialId,
            externalReference,
            fiatAmount,
            context,
            returnUrl,
            payerPageIdGenerator.Generate(),
            createdAt,
            _options.PaymentExpiration,
            _options.LateAcceptanceWindow);

        var paymentDraft = ToPaymentDraft(payment);
        var eventDrafts = payment.Events.Select(ToPaymentEventDraft).ToArray();
        var webhookEvents = eventDrafts
            .Select(paymentEvent => ToWebhookOutboxEventDraft(
                paymentEvent,
                payment.Id,
                integrationApiCredentialId))
            .ToArray();
        var optionDrafts = new List<PaymentOptionDraft>();
        foreach (var currency in SupportedCurrencies)
        {
            var rateAvailable = await exchangeRateSource.IsRateAvailableAsync(
                fiatAmount.Currency,
                currency,
                createdAt,
                cancellationToken);
            var addressAvailable = await paymentAddressProvider.IsAddressAvailableAsync(
                currency,
                cancellationToken);
            var observationAvailable =
                await blockchainObservationAdapter.IsObservationAvailableAsync(
                    currency,
                    cancellationToken);
            var optionAvailable = rateAvailable && addressAvailable && observationAvailable;
            optionDrafts.Add(new PaymentOptionDraft(
                payment.Id,
                currency,
                optionAvailable ? "available" : "unavailable",
                !rateAvailable
                    ? PaymentOptionUnavailableReasons.ExchangeRate
                    : !addressAvailable
                        ? PaymentOptionUnavailableReasons.PaymentAddress
                        : !observationAvailable
                            ? PaymentOptionUnavailableReasons.BlockchainObservation
                            : null,
                createdAt));
        }
        var requestHash = ComputeRequestHash(paymentDraft);

        var storeResult = await paymentStore.CreateAsync(
            integrationApiCredentialId,
            idempotencyKey,
            requestHash,
            paymentDraft,
            optionDrafts,
            eventDrafts,
            webhookEvents,
            cancellationToken);

        return storeResult.Kind switch
        {
            CreatePaymentStoreResultKind.Created => CreatePaymentResult.Created(ToResponse(storeResult.Payment!)),
            CreatePaymentStoreResultKind.Existing => CreatePaymentResult.Existing(ToResponse(storeResult.Payment!)),
            CreatePaymentStoreResultKind.IdempotencyConflict => CreatePaymentResult.IdempotencyConflict(),
            CreatePaymentStoreResultKind.ProjectUnavailable => CreatePaymentResult.ProjectUnavailable(),
            _ => throw new InvalidOperationException($"Unsupported store result {storeResult.Kind}."),
        };
    }

    public async Task<PaymentResponse?> FindAsync(
        Guid integrationApiCredentialId,
        Guid paymentId,
        CancellationToken cancellationToken)
    {
        var payment = await paymentStore.FindAsync(integrationApiCredentialId, paymentId, cancellationToken);
        return payment is null ? null : ToResponse(payment);
    }

    public async Task<PaymentResponse?> FindByPayerPageIdAsync(
        string payerPageId,
        CancellationToken cancellationToken)
    {
        var normalizedPayerPageId = NormalizePayerPageId(payerPageId);
        var payment = await paymentStore.FindByPayerPageIdAsync(normalizedPayerPageId, cancellationToken);
        return payment is null ? null : ToResponse(payment);
    }

    public async Task<SelectPaymentCurrencyResult> SelectCurrencyAsync(
        SelectPaymentCurrencyCommand command,
        CancellationToken cancellationToken)
    {
        var payerPageId = NormalizePayerPageId(command.PayerPageId);
        var supportedCurrency = NormalizeSupportedCurrency(command.SupportedCurrency);
        if (!SupportedCurrencies.Contains(supportedCurrency, StringComparer.Ordinal))
        {
            return SelectPaymentCurrencyResult.UnsupportedCurrency();
        }

        var payment = await paymentStore.FindByPayerPageIdAsync(payerPageId, cancellationToken);
        if (payment is null)
        {
            return SelectPaymentCurrencyResult.NotFound();
        }

        if (payment.SelectedCurrency is not null)
        {
            return SelectPaymentCurrencyResult.AlreadySelected(ToResponse(payment));
        }

        var selectedOption = payment.PaymentOptions?
            .SingleOrDefault(option =>
                StringComparer.Ordinal.Equals(option.SupportedCurrency, supportedCurrency));
        if (selectedOption is not null && selectedOption.Status != "available")
        {
            // The Payment Option already records why it is unavailable.
            // Collapsing every reason into "rate unavailable" points operators
            // at the Exchange Rate provider when the real gap is a missing
            // address source or a failing Blockchain Observation provider.
            return selectedOption.UnavailableReason switch
            {
                PaymentOptionUnavailableReasons.PaymentAddress =>
                    SelectPaymentCurrencyResult.PaymentAddressUnavailable(),
                PaymentOptionUnavailableReasons.BlockchainObservation =>
                    SelectPaymentCurrencyResult.ObservationUnavailable(),
                _ => SelectPaymentCurrencyResult.RateUnavailable(),
            };
        }

        var selectedAt = clock.UtcNow;
        if (selectedAt >= payment.ExpiresAt)
        {
            return SelectPaymentCurrencyResult.PaymentExpired();
        }

        var quote = await exchangeRateSource.GetRateLockQuoteAsync(
            payment.FiatCurrency,
            payment.FiatAmountMinor,
            supportedCurrency,
            selectedAt,
            cancellationToken);
        if (quote is null)
        {
            return SelectPaymentCurrencyResult.RateUnavailable();
        }

        var address = await paymentAddressProvider.AssignAsync(
            payment.Id,
            supportedCurrency,
            cancellationToken);
        if (address is null)
        {
            return SelectPaymentCurrencyResult.PaymentAddressUnavailable();
        }

        var selection = new PaymentSelectionDraft(
            payment.Id,
            supportedCurrency,
            quote.ExpectedCryptoAmount,
            address.PaymentAddress,
            quote.RateSource,
            quote.RateValue,
            quote.ObservedAt,
            selectedAt);
        var storeResult = await paymentStore.SelectCurrencyAsync(
            selection,
            new PaymentEventDraft(
                Guid.NewGuid(),
                payment.Id,
                "payment.currency_selected",
                selectedAt,
                Details: null),
            ToWebhookOutboxEventDraft(
                payment.Id,
                payment.IntegrationApiCredentialId,
                "payment.currency_selected",
                selectedAt),
            cancellationToken);

        if (storeResult.Kind == SelectCurrencyStoreResultKind.Selected &&
            storeResult.Payment is not null)
        {
            await blockchainObservationAdapter.StartWatchingAsync(
                new BlockchainObservationTarget(
                    payment.Id,
                    supportedCurrency,
                    address.PaymentAddress,
                    quote.ExpectedCryptoAmount),
                cancellationToken);
        }

        return storeResult.Kind switch
        {
            SelectCurrencyStoreResultKind.Selected =>
                SelectPaymentCurrencyResult.Selected(ToResponse(storeResult.Payment!)),
            SelectCurrencyStoreResultKind.AlreadySelected =>
                SelectPaymentCurrencyResult.AlreadySelected(ToResponse(storeResult.Payment!)),
            SelectCurrencyStoreResultKind.NotFound =>
                SelectPaymentCurrencyResult.NotFound(),
            _ => throw new InvalidOperationException($"Unsupported select result {storeResult.Kind}."),
        };
    }

    public async Task<RecordBlockchainObservationResult> RecordBlockchainObservationAsync(
        RecordBlockchainObservationCommand command,
        CancellationToken cancellationToken)
    {
        if (command.PaymentId == Guid.Empty)
        {
            throw new DomainRuleException("Payment identifier is required.", "payment_id.required");
        }

        var supportedCurrency = NormalizeSupportedCurrency(command.SupportedCurrency);
        var paymentAddress = NormalizeRequiredText(
            command.PaymentAddress,
            "Payment Address is required.",
            "payment_address.required");
        var transactionHash = NormalizeRequiredText(
            command.TransactionHash,
            "Transaction Hash is required.",
            "transaction_hash.required");
        var observedAmount = NormalizePositiveDecimalText(command.ObservedAmount);
        var providerName = NormalizeRequiredText(
            command.ProviderName,
            "Blockchain Observation provider name is required.",
            "provider_name.required");
        var providerObservationId = string.IsNullOrWhiteSpace(command.ProviderObservationId)
            ? null
            : command.ProviderObservationId.Trim();
        if (command.Confirmations < 0)
        {
            throw new DomainRuleException(
                "Confirmation count must not be negative.",
                "confirmations.negative");
        }

        var recordedAt = clock.UtcNow;
        var observation = new BlockchainObservationDraft(
            Guid.NewGuid(),
            command.PaymentId,
            supportedCurrency,
            paymentAddress,
            transactionHash,
            observedAmount,
            command.ObservedAt,
            recordedAt,
            command.Confirmations,
            providerName,
            providerObservationId,
            recordedAt,
            recordedAt);

        var storeResult = await paymentStore.RecordBlockchainObservationAsync(
            observation,
            new PaymentCompletionPolicyDraft(
                GetRequiredConfirmations(supportedCurrency),
                _options.PaymentTolerancePercent),
            new PaymentEventDraft(
                Guid.NewGuid(),
                command.PaymentId,
                "payment.observed",
                recordedAt,
                Details: null),
            ToWebhookOutboxEventDraft(
                command.PaymentId,
                integrationApiCredentialId: Guid.Empty,
                "payment.observed",
                recordedAt),
            new PaymentEventDraft(
                Guid.NewGuid(),
                command.PaymentId,
                "payment.completed",
                recordedAt,
                Details: null),
            ToWebhookOutboxEventDraft(
                command.PaymentId,
                integrationApiCredentialId: Guid.Empty,
                "payment.completed",
                recordedAt),
            cancellationToken);

        return storeResult.Kind switch
        {
            RecordBlockchainObservationStoreResultKind.Observed =>
                RecordBlockchainObservationResult.Observed(ToResponse(storeResult.Payment!)),
            RecordBlockchainObservationStoreResultKind.Completed =>
                RecordBlockchainObservationResult.Completed(ToResponse(storeResult.Payment!)),
            RecordBlockchainObservationStoreResultKind.AlreadyRecorded =>
                RecordBlockchainObservationResult.AlreadyRecorded(ToResponse(storeResult.Payment!)),
            RecordBlockchainObservationStoreResultKind.PaymentNotFound =>
                RecordBlockchainObservationResult.PaymentNotFound(),
            RecordBlockchainObservationStoreResultKind.PaymentNotReady =>
                RecordBlockchainObservationResult.PaymentNotReady(),
            RecordBlockchainObservationStoreResultKind.ObservationMismatch =>
                RecordBlockchainObservationResult.ObservationMismatch(),
            _ => throw new InvalidOperationException($"Unsupported observation result {storeResult.Kind}."),
        };
    }

    public async Task<ExpireDuePaymentsResult> ExpireDuePaymentsAsync(
        int maxPayments,
        CancellationToken cancellationToken)
    {
        if (maxPayments <= 0)
        {
            throw new DomainRuleException(
                "Expiration batch size must be greater than zero.",
                "expiration_batch_size.not_positive");
        }

        var result = await paymentStore.ExpireDuePaymentsAsync(
            clock.UtcNow,
            maxPayments,
            cancellationToken);
        return new ExpireDuePaymentsResult(result.ExpiredCount);
    }

    public async Task<PollBlockchainObservationsResult> PollBlockchainObservationsAsync(
        int maxPayments,
        CancellationToken cancellationToken)
    {
        if (maxPayments <= 0)
        {
            throw new DomainRuleException(
                "Observation batch size must be greater than zero.",
                "observation_batch_size.not_positive");
        }

        var targets = await paymentStore.ListBlockchainObservationTargetsAsync(
            clock.UtcNow,
            maxPayments,
            cancellationToken);
        var observationCount = 0;
        var recordedCount = 0;
        var completedCount = 0;
        var alreadyRecordedCount = 0;
        var rejectedCount = 0;

        foreach (var target in targets)
        {
            var observations = await blockchainObservationAdapter.PollAsync(target, cancellationToken);
            observationCount += observations.Count;
            foreach (var observation in observations)
            {
                var result = await RecordBlockchainObservationAsync(
                    new RecordBlockchainObservationCommand(
                        target.PaymentId,
                        target.SupportedCurrency,
                        target.PaymentAddress,
                        observation.TransactionHash,
                        observation.ObservedAmount,
                        observation.ObservedAt,
                        observation.Confirmations,
                        observation.ProviderName,
                        observation.ProviderObservationId),
                    cancellationToken);

                switch (result.Kind)
                {
                    case RecordBlockchainObservationResultKind.Observed:
                        recordedCount++;
                        break;
                    case RecordBlockchainObservationResultKind.Completed:
                        recordedCount++;
                        completedCount++;
                        break;
                    case RecordBlockchainObservationResultKind.AlreadyRecorded:
                        alreadyRecordedCount++;
                        break;
                    case RecordBlockchainObservationResultKind.PaymentNotFound:
                    case RecordBlockchainObservationResultKind.PaymentNotReady:
                    case RecordBlockchainObservationResultKind.ObservationMismatch:
                        rejectedCount++;
                        break;
                    default:
                        throw new InvalidOperationException($"Unsupported observation result {result.Kind}.");
                }
            }
        }

        return new PollBlockchainObservationsResult(
            targets.Count,
            observationCount,
            recordedCount,
            completedCount,
            alreadyRecordedCount,
            rejectedCount);
    }

    public async Task<MonitorBlockchainReorgsResult> MonitorBlockchainReorgsAsync(
        int maxTransactions,
        CancellationToken cancellationToken)
    {
        if (maxTransactions <= 0)
        {
            throw new DomainRuleException(
                "Reorg monitoring batch size must be greater than zero.",
                "reorg_monitoring_batch_size.not_positive");
        }

        var targets = await paymentStore.ListBlockchainReorgMonitoringTargetsAsync(
            new ReorgMonitoringPolicyDraft(
                GetRequiredConfirmations("BTC"),
                Math.Max(0, _options.BtcReorgMonitoringDepth),
                GetRequiredConfirmations("LTC"),
                Math.Max(0, _options.LtcReorgMonitoringDepth),
                GetRequiredConfirmations("ETH"),
                Math.Max(0, _options.EthReorgMonitoringDepth)),
            maxTransactions,
            cancellationToken);

        var checkedCount = 0;
        var updatedCount = 0;
        var reorgAlertCount = 0;
        var missingObservationCount = 0;
        foreach (var target in targets)
        {
            var observations = await blockchainObservationAdapter.PollAsync(
                new BlockchainObservationTarget(
                    target.PaymentId,
                    target.SupportedCurrency,
                    target.PaymentAddress,
                    target.ExpectedCryptoAmount),
                cancellationToken);
            checkedCount++;

            var observation = observations.FirstOrDefault(candidate =>
                StringComparer.Ordinal.Equals(candidate.TransactionHash, target.TransactionHash));
            if (observation is null)
            {
                missingObservationCount++;
                continue;
            }

            var updateResult = await UpdateBlockchainTransactionConfirmationsAsync(
                new UpdateBlockchainTransactionConfirmationsCommand(
                    target.PaymentId,
                    target.SupportedCurrency,
                    target.TransactionHash,
                    observation.Confirmations,
                    BlockHash: null,
                    BlockHeight: null),
                cancellationToken);

            switch (updateResult.Kind)
            {
                case UpdateBlockchainTransactionConfirmationsResultKind.Updated:
                case UpdateBlockchainTransactionConfirmationsResultKind.Completed:
                    updatedCount++;
                    break;
                case UpdateBlockchainTransactionConfirmationsResultKind.ReorgAlerted:
                    reorgAlertCount++;
                    break;
                case UpdateBlockchainTransactionConfirmationsResultKind.PaymentNotFound:
                case UpdateBlockchainTransactionConfirmationsResultKind.PaymentNotReady:
                case UpdateBlockchainTransactionConfirmationsResultKind.TransactionNotFound:
                    missingObservationCount++;
                    break;
                default:
                    throw new InvalidOperationException($"Unsupported confirmation update result {updateResult.Kind}.");
            }
        }

        return new MonitorBlockchainReorgsResult(
            targets.Count,
            checkedCount,
            updatedCount,
            reorgAlertCount,
            missingObservationCount);
    }

    public async Task<UpdateBlockchainTransactionConfirmationsResult> UpdateBlockchainTransactionConfirmationsAsync(
        UpdateBlockchainTransactionConfirmationsCommand command,
        CancellationToken cancellationToken)
    {
        if (command.PaymentId == Guid.Empty)
        {
            throw new DomainRuleException("Payment identifier is required.", "payment_id.required");
        }

        var supportedCurrency = NormalizeSupportedCurrency(command.SupportedCurrency);
        var transactionHash = NormalizeRequiredText(
            command.TransactionHash,
            "Transaction Hash is required.",
            "transaction_hash.required");
        if (command.Confirmations < 0)
        {
            throw new DomainRuleException(
                "Confirmation count must not be negative.",
                "confirmations.negative");
        }

        var checkedAt = clock.UtcNow;
        var storeResult = await paymentStore.UpdateBlockchainTransactionConfirmationsAsync(
            new BlockchainTransactionConfirmationUpdateDraft(
                command.PaymentId,
                supportedCurrency,
                transactionHash,
                command.Confirmations,
                string.IsNullOrWhiteSpace(command.BlockHash) ? null : command.BlockHash.Trim(),
                command.BlockHeight,
                checkedAt),
            new PaymentCompletionPolicyDraft(
                GetRequiredConfirmations(supportedCurrency),
                _options.PaymentTolerancePercent),
            new PaymentEventDraft(
                Guid.NewGuid(),
                command.PaymentId,
                "payment.completed",
                checkedAt,
                Details: null),
            ToWebhookOutboxEventDraft(
                command.PaymentId,
                integrationApiCredentialId: Guid.Empty,
                "payment.completed",
                checkedAt),
            new PaymentEventDraft(
                Guid.NewGuid(),
                command.PaymentId,
                "payment.reorg_alerted",
                checkedAt,
                Details: null),
            cancellationToken);

        return storeResult.Kind switch
        {
            UpdateBlockchainTransactionConfirmationsStoreResultKind.Updated =>
                UpdateBlockchainTransactionConfirmationsResult.Updated(ToResponse(storeResult.Payment!)),
            UpdateBlockchainTransactionConfirmationsStoreResultKind.Completed =>
                UpdateBlockchainTransactionConfirmationsResult.Completed(ToResponse(storeResult.Payment!)),
            UpdateBlockchainTransactionConfirmationsStoreResultKind.ReorgAlerted =>
                UpdateBlockchainTransactionConfirmationsResult.ReorgAlerted(ToResponse(storeResult.Payment!)),
            UpdateBlockchainTransactionConfirmationsStoreResultKind.PaymentNotFound =>
                UpdateBlockchainTransactionConfirmationsResult.PaymentNotFound(),
            UpdateBlockchainTransactionConfirmationsStoreResultKind.PaymentNotReady =>
                UpdateBlockchainTransactionConfirmationsResult.PaymentNotReady(),
            UpdateBlockchainTransactionConfirmationsStoreResultKind.TransactionNotFound =>
                UpdateBlockchainTransactionConfirmationsResult.TransactionNotFound(),
            _ => throw new InvalidOperationException($"Unsupported confirmation update result {storeResult.Kind}."),
        };
    }

    private static string NormalizeIdempotencyKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainRuleException("Idempotency Key is required.", "idempotency_key.required");
        }

        var trimmedValue = value.Trim();
        if (trimmedValue.Length > 255)
        {
            throw new DomainRuleException(
                "Idempotency Key must be at most 255 characters.",
                "idempotency_key.too_long");
        }

        return trimmedValue;
    }

    private static Uri? NormalizeReturnUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmedValue = value.Trim();
        if (trimmedValue.Length > 2048 ||
            !Uri.TryCreate(trimmedValue, UriKind.Absolute, out var returnUrl))
        {
            throw new DomainRuleException("Return URL must be absolute.", "return_url.invalid");
        }

        return returnUrl;
    }

    private static string NormalizePayerPageId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainRuleException("Payer Page identifier is required.", "payer_page_id.required");
        }

        var trimmedValue = value.Trim();
        if (trimmedValue.Length > 255)
        {
            throw new DomainRuleException(
                "Payer Page identifier must be at most 255 characters.",
                "payer_page_id.too_long");
        }

        return trimmedValue;
    }

    private static string NormalizeSupportedCurrency(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainRuleException("Supported Currency is required.", "supported_currency.required");
        }

        return value.Trim().ToUpperInvariant();
    }

    private int GetRequiredConfirmations(string supportedCurrency)
    {
        return supportedCurrency switch
        {
            "BTC" => _options.BtcConfirmationRequirement,
            "LTC" => _options.LtcConfirmationRequirement,
            "ETH" => _options.EthConfirmationRequirement,
            _ => throw new InvalidOperationException($"Unsupported currency {supportedCurrency}."),
        };
    }

    private static string NormalizeRequiredText(string? value, string message, string code)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new DomainRuleException(message, code);
        }

        return value.Trim();
    }

    private static string NormalizePositiveDecimalText(string? value)
    {
        var amountText = NormalizeRequiredText(
            value,
            "Observed amount is required.",
            "observed_amount.required");

        if (!decimal.TryParse(
                amountText,
                NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture,
                out var amount) ||
            amount <= 0)
        {
            throw new DomainRuleException(
                "Observed amount must be a positive decimal value.",
                "observed_amount.invalid");
        }

        return amountText;
    }

    private static PaymentDraft ToPaymentDraft(Payment payment)
    {
        return new PaymentDraft(
            payment.Id,
            payment.IntegrationApiCredentialId,
            payment.ExternalReference.Value,
            payment.FiatAmount.Currency,
            payment.FiatAmount.MinorUnits,
            payment.Status.Value,
            payment.PayerPageId,
            payment.ExpiresAt,
            payment.LateAcceptanceEndsAt,
            payment.PaymentContext.Username,
            payment.PaymentContext.CustomerNumber,
            payment.PaymentContext.CartName,
            payment.PaymentContext.Note,
            payment.ReturnUrl?.ToString(),
            payment.CreatedAt,
            payment.UpdatedAt);
    }

    private static PaymentEventDraft ToPaymentEventDraft(PaymentEvent paymentEvent)
    {
        return new PaymentEventDraft(
            paymentEvent.Id,
            paymentEvent.PaymentId,
            paymentEvent.EventType,
            paymentEvent.OccurredAt,
            Details: null);
    }

    private static WebhookOutboxEventDraft ToWebhookOutboxEventDraft(
        PaymentEventDraft paymentEvent,
        Guid paymentId,
        Guid integrationApiCredentialId)
    {
        return ToWebhookOutboxEventDraft(
            paymentId,
            integrationApiCredentialId,
            paymentEvent.EventType,
            paymentEvent.OccurredAt);
    }

    private static WebhookOutboxEventDraft ToWebhookOutboxEventDraft(
        Guid paymentId,
        Guid integrationApiCredentialId,
        string eventType,
        DateTimeOffset occurredAt)
    {
        var eventId = Guid.NewGuid();
        return new WebhookOutboxEventDraft(
            eventId,
            paymentId,
            integrationApiCredentialId,
            eventType,
            EventVersion: "1",
            PayloadVersion: 1,
            ResourceType: "payment",
            ResourceId: paymentId.ToString("D"),
            Status: "pending",
            occurredAt,
            CreatedAt: occurredAt,
            NextAttemptAt: occurredAt,
            AttemptCount: 0,
            CorrelationId: eventId.ToString("D"));
    }

    private string ComputeRequestHash(PaymentDraft payment)
    {
        var requestFingerprint = new
        {
            payment.FiatCurrency,
            payment.FiatAmountMinor,
            payment.ExternalReference,
            PaymentContext = new
            {
                Username = payment.ContextUsername,
                CustomerNumber = payment.ContextCustomerNumber,
                CartName = payment.ContextCartName,
                Note = payment.ContextNote,
            },
            payment.ReturnUrl,
        };

        var json = JsonSerializer.Serialize(requestFingerprint);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(json));
        return "sha256:" + Convert.ToHexString(hash).ToLowerInvariant();
    }

    private PaymentResponse ToResponse(PaymentReadModel payment)
    {
        return new PaymentResponse(
            payment.Id,
            payment.Status,
            BuildPayerPageUrl(payment.PayerPageId),
            payment.ExpiresAt,
            payment.FiatCurrency,
            payment.FiatAmountMinor,
            payment.ExternalReference,
            payment.SelectedCurrency,
            payment.ExpectedCryptoAmount,
            payment.PaymentAddress,
            payment.ObservedTotal,
            payment.CompletedAt,
            payment.SettledAt,
            payment.ReturnUrl,
            PaymentOptions: (payment.PaymentOptions ?? [])
                .Select(option => new PaymentOptionResponse(
                    option.SupportedCurrency,
                    option.Status,
                    option.UnavailableReason))
                .ToArray());
    }

    private string BuildPayerPageUrl(string payerPageId)
    {
        var baseUrl = _options.PayerPageBaseUrl.TrimEnd('/');
        return $"{baseUrl}/{Uri.EscapeDataString(payerPageId)}";
    }
}
