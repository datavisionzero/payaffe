using Payaffe.Application.Payments;
using Payaffe.Domain.Payments;
using Microsoft.Extensions.Options;

namespace Payaffe.Application.Tests.Payments;

public sealed class PaymentApplicationServiceTests
{
    private static readonly Guid PaymentId = Guid.Parse("0ba93cf2-2404-4870-8d34-399147ef30fb");
    private static readonly Guid CredentialId = Guid.Parse("f1f9d60f-78a2-4a5c-b8f0-4fd3da6ca84b");
    private static readonly Guid ProjectId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    [Fact]
    public async Task Create_hashes_payment_creation_request_without_server_timing_options()
    {
        var command = new CreatePaymentCommand(
            "EUR",
            1999,
            "order-123",
            new PaymentContextCommand("customer@example.test", "C-1000", "Starter", "Note"),
            "https://example.test/orders/order-123",
            "same-request");
        var storeWithOneHourExpiration = new CapturingPaymentStore();
        var storeWithTwoHourExpiration = new CapturingPaymentStore();

        await CreateService(storeWithOneHourExpiration, TimeSpan.FromHours(1)).CreateAsync(
            Guid.NewGuid(),
            command,
            CancellationToken.None);
        await CreateService(storeWithTwoHourExpiration, TimeSpan.FromHours(2)).CreateAsync(
            Guid.NewGuid(),
            command,
            CancellationToken.None);

        Assert.Equal(storeWithOneHourExpiration.RequestHash, storeWithTwoHourExpiration.RequestHash);
    }

    [Fact]
    public async Task Create_records_rate_availability_for_each_payment_option()
    {
        var store = new CapturingPaymentStore();
        var service = CreateService(
            store,
            TimeSpan.FromHours(1),
            new SelectiveExchangeRateSource("BTC", "ETH"),
            new FixedPaymentAddressProvider(),
            new CapturingBlockchainObservationAdapter());

        await service.CreateAsync(
            CredentialId,
            new CreatePaymentCommand(
                "EUR",
                1999,
                "order-123",
                PaymentContext: null,
                ReturnUrl: null,
                "rate-options"),
            CancellationToken.None);

        Assert.Collection(
            store.PaymentOptions,
            option =>
            {
                Assert.Equal("BTC", option.SupportedCurrency);
                Assert.Equal("available", option.Status);
                Assert.Null(option.UnavailableReason);
            },
            option =>
            {
                Assert.Equal("LTC", option.SupportedCurrency);
                Assert.Equal("unavailable", option.Status);
                Assert.Equal("exchange_rate.unavailable", option.UnavailableReason);
            },
            option =>
            {
                Assert.Equal("ETH", option.SupportedCurrency);
                Assert.Equal("available", option.Status);
                Assert.Null(option.UnavailableReason);
            });
    }

    [Fact]
    public async Task Create_records_address_availability_without_disabling_other_options()
    {
        var store = new CapturingPaymentStore();
        var service = CreateService(
            store,
            TimeSpan.FromHours(1),
            new FixedExchangeRateSource(),
            new SelectivePaymentAddressProvider("BTC", "LTC"),
            new CapturingBlockchainObservationAdapter());

        await service.CreateAsync(
            CredentialId,
            new CreatePaymentCommand(
                "EUR",
                1999,
                "order-124",
                PaymentContext: null,
                ReturnUrl: null,
                "address-options"),
            CancellationToken.None);

        Assert.Equal("available", store.PaymentOptions.Single(option =>
            option.SupportedCurrency == "BTC").Status);
        var eth = store.PaymentOptions.Single(option =>
            option.SupportedCurrency == "ETH");
        Assert.Equal("unavailable", eth.Status);
        Assert.Equal("payment_address.unavailable", eth.UnavailableReason);
    }

    [Fact]
    public async Task Create_records_observation_unavailability_per_option()
    {
        var store = new CapturingPaymentStore();
        var service = CreateService(
            store,
            TimeSpan.FromHours(1),
            new FixedExchangeRateSource(),
            new FixedPaymentAddressProvider(),
            new SelectiveBlockchainObservationAdapter("BTC", "ETH"));

        await service.CreateAsync(
            CredentialId,
            new CreatePaymentCommand(
                "EUR",
                1999,
                "order-125",
                PaymentContext: null,
                ReturnUrl: null,
                "observation-options"),
            CancellationToken.None);

        Assert.Equal("available", store.PaymentOptions.Single(option =>
            option.SupportedCurrency == "BTC").Status);
        var ltc = store.PaymentOptions.Single(option =>
            option.SupportedCurrency == "LTC");
        Assert.Equal("unavailable", ltc.Status);
        Assert.Equal("blockchain_observation.unavailable", ltc.UnavailableReason);
    }

    [Fact]
    public async Task Select_currency_records_rate_lock_address_assignment_and_observation_target()
    {
        var store = new CapturingSelectionStore();
        var observation = new CapturingBlockchainObservationAdapter();
        var service = CreateService(
            store,
            TimeSpan.FromHours(1),
            new FixedExchangeRateSource(),
            new FixedPaymentAddressProvider(),
            observation);

        var result = await service.SelectCurrencyAsync(
            new SelectPaymentCurrencyCommand("payer-page-id", "btc"),
            CancellationToken.None);

        Assert.Equal(SelectPaymentCurrencyResultKind.Selected, result.Kind);
        Assert.NotNull(result.Payment);
        Assert.Equal("waiting_for_payment", result.Payment.Status);
        Assert.Equal("BTC", result.Payment.SelectedCurrency);
        Assert.Equal("0.00039980", result.Payment.ExpectedCryptoAmount);
        Assert.Equal("btc-test-address", result.Payment.PaymentAddress);

        Assert.NotNull(store.Selection);
        Assert.Equal(PaymentId, store.Selection.PaymentId);
        Assert.Equal("BTC", store.Selection.SupportedCurrency);
        Assert.Equal("test-rate-source", store.Selection.RateSource);
        Assert.Equal("50000.00", store.Selection.RateValue);
        Assert.Equal("payment.currency_selected", store.PaymentEvent!.EventType);
        Assert.Equal("payment.currency_selected", store.WebhookEvent!.EventType);
        Assert.Equal("1", store.WebhookEvent.EventVersion);
        Assert.Equal("payment", store.WebhookEvent.ResourceType);
        Assert.Equal(PaymentId.ToString("D"), store.WebhookEvent.ResourceId);
        Assert.Equal("pending", store.WebhookEvent.Status);

        Assert.NotNull(observation.Target);
        Assert.Equal(PaymentId, observation.Target.PaymentId);
        Assert.Equal("BTC", observation.Target.SupportedCurrency);
        Assert.Equal("btc-test-address", observation.Target.PaymentAddress);
        Assert.Equal("0.00039980", observation.Target.ExpectedCryptoAmount);
    }

    /// <summary>
    /// The selection is committed before watching starts. A provider failure
    /// at that point must not turn a saved selection into a server error.
    /// </summary>
    [Fact]
    public async Task Select_currency_succeeds_when_starting_to_watch_fails_after_the_selection_is_saved()
    {
        var store = new CapturingSelectionStore();
        var service = CreateService(
            store,
            TimeSpan.FromHours(1),
            new FixedExchangeRateSource(),
            new FixedPaymentAddressProvider(),
            new FailingWatchAdapter());

        var result = await service.SelectCurrencyAsync(
            new SelectPaymentCurrencyCommand("payer-page-id", "btc"),
            CancellationToken.None);

        Assert.Equal(SelectPaymentCurrencyResultKind.Selected, result.Kind);
        Assert.NotNull(store.Selection);
    }

    [Fact]
    public async Task Select_currency_rechecks_a_transiently_unavailable_payment_option()
    {
        var store = new CapturingSelectionStore(
            optionAvailable: false,
            PaymentOptionUnavailableReasons.ExchangeRate);
        var service = CreateService(
            store,
            TimeSpan.FromHours(1),
            new FixedExchangeRateSource(),
            new FixedPaymentAddressProvider(),
            new CapturingBlockchainObservationAdapter());

        var result = await service.SelectCurrencyAsync(
            new SelectPaymentCurrencyCommand("payer-page-id", "BTC"),
            CancellationToken.None);

        Assert.Equal(SelectPaymentCurrencyResultKind.Selected, result.Kind);
        Assert.NotNull(store.Selection);
    }

    [Fact]
    public async Task Select_currency_rechecks_blockchain_observation_before_assigning_an_address()
    {
        var store = new CapturingSelectionStore();
        var service = CreateService(
            store,
            TimeSpan.FromHours(1),
            new FixedExchangeRateSource(),
            new FixedPaymentAddressProvider(),
            new SelectiveBlockchainObservationAdapter("LTC", "ETH"));

        var result = await service.SelectCurrencyAsync(
            new SelectPaymentCurrencyCommand("payer-page-id", "BTC"),
            CancellationToken.None);

        Assert.Equal(SelectPaymentCurrencyResultKind.ObservationUnavailable, result.Kind);
        Assert.Null(store.Selection);
    }

    /// <summary>
    /// A Payment is always looked up inside its Project. An empty Project must
    /// be refused rather than read as "any Project".
    /// </summary>
    [Fact]
    public async Task Observation_and_confirmation_updates_refuse_an_empty_project()
    {
        var observationStore = new CapturingObservationStore();
        var confirmationStore = new CapturingConfirmationUpdateStore();

        var observation = await Assert.ThrowsAsync<DomainRuleException>(() =>
            CreateService(observationStore, TimeSpan.FromHours(1)).RecordBlockchainObservationAsync(
                new RecordBlockchainObservationCommand(
                    PaymentId,
                    "btc",
                    "btc-test-address",
                    "tx-123",
                    "0.00039980",
                    DateTimeOffset.Parse("2026-07-04T12:05:00Z"),
                    Confirmations: 0,
                    "test-provider",
                    "provider-observation-123",
                    ProjectId: Guid.Empty),
                CancellationToken.None));
        var confirmation = await Assert.ThrowsAsync<DomainRuleException>(() =>
            CreateService(confirmationStore, TimeSpan.FromHours(1)).UpdateBlockchainTransactionConfirmationsAsync(
                new UpdateBlockchainTransactionConfirmationsCommand(
                    PaymentId,
                    "btc",
                    "tx-123",
                    Confirmations: 1,
                    BlockHash: null,
                    BlockHeight: null,
                    ProjectId: Guid.Empty),
                CancellationToken.None));

        Assert.Equal("project_id.required", observation.Code);
        Assert.Equal("project_id.required", confirmation.Code);
        Assert.Null(observationStore.Observation);
        Assert.Null(confirmationStore.ConfirmationUpdate);
    }

    [Fact]
    public async Task Record_blockchain_observation_records_matching_transaction_event_and_webhook()
    {
        var store = new CapturingObservationStore();
        var service = CreateService(store, TimeSpan.FromHours(1));

        var result = await service.RecordBlockchainObservationAsync(
            new RecordBlockchainObservationCommand(
                PaymentId,
                "btc",
                "btc-test-address",
                "tx-123",
                "0.00039980",
                DateTimeOffset.Parse("2026-07-04T12:05:00Z"),
                Confirmations: 0,
                "test-provider",
                "provider-observation-123",
                ProjectId: ProjectId),
            CancellationToken.None);

        Assert.Equal(RecordBlockchainObservationResultKind.Observed, result.Kind);
        Assert.Equal("observed", result.Payment!.Status);
        Assert.Equal("0.0003998", result.Payment.ObservedTotal);

        Assert.NotNull(store.Observation);
        Assert.Equal(PaymentId, store.Observation.PaymentId);
        Assert.Equal("BTC", store.Observation.SupportedCurrency);
        Assert.Equal("btc-test-address", store.Observation.PaymentAddress);
        Assert.Equal("tx-123", store.Observation.TransactionHash);
        Assert.Equal("0.00039980", store.Observation.ObservedAmount);
        Assert.Equal(0, store.Observation.Confirmations);
        Assert.Equal("test-provider", store.Observation.ProviderName);
        Assert.Equal(1, store.CompletionPolicy!.RequiredConfirmations);
        Assert.Equal(1.0m, store.CompletionPolicy.PaymentTolerancePercent);
        Assert.Equal("payment.observed", store.PaymentEvent!.EventType);
        Assert.Equal("payment.observed", store.WebhookEvent!.EventType);
        Assert.Equal("pending", store.WebhookEvent.Status);
        Assert.Equal("payment.completed", store.CompletedPaymentEvent!.EventType);
        Assert.Equal("payment.completed", store.CompletedWebhookEvent!.EventType);
    }

    [Fact]
    public async Task Record_blockchain_observation_returns_completed_result_when_store_completes_payment()
    {
        var store = new CapturingObservationStore(completePayment: true);
        var service = CreateService(store, TimeSpan.FromHours(1));

        var result = await service.RecordBlockchainObservationAsync(
            new RecordBlockchainObservationCommand(
                PaymentId,
                "btc",
                "btc-test-address",
                "tx-123",
                "0.00039980",
                DateTimeOffset.Parse("2026-07-04T12:05:00Z"),
                Confirmations: 1,
                "test-provider",
                "provider-observation-123",
                ProjectId: ProjectId),
            CancellationToken.None);

        Assert.Equal(RecordBlockchainObservationResultKind.Completed, result.Kind);
        Assert.Equal("completed", result.Payment!.Status);
        Assert.Equal(DateTimeOffset.Parse("2026-07-04T12:00:00Z"), result.Payment.CompletedAt);
    }

    [Fact]
    public async Task Poll_blockchain_observations_records_adapter_observations()
    {
        var store = new CapturingObservationStore();
        store.AddObservationTarget(new BlockchainObservationTarget(
            PaymentId,
            "BTC",
            "btc-test-address",
            "0.00039980",
            ProjectId));
        var observation = new CapturingBlockchainObservationAdapter();
        observation.AddObservation(new BlockchainObservation(
            "tx-123",
            "0.00039980",
            DateTimeOffset.Parse("2026-07-04T12:05:00Z"),
            Confirmations: 0,
            "test-provider",
            "provider-observation-123"));
        var service = CreateService(
            store,
            TimeSpan.FromHours(1),
            new FixedExchangeRateSource(),
            new FixedPaymentAddressProvider(),
            observation);

        var result = await service.PollBlockchainObservationsAsync(25, CancellationToken.None);

        Assert.Equal(1, result.TargetCount);
        Assert.Equal(1, result.ObservationCount);
        Assert.Equal(1, result.RecordedCount);
        Assert.Equal(0, result.CompletedCount);
        Assert.Equal(0, result.AlreadyRecordedCount);
        Assert.Equal(0, result.RejectedCount);
        Assert.Equal(DateTimeOffset.Parse("2026-07-04T12:00:00Z"), store.ObservedUntil);
        Assert.Equal(25, store.MaxObservationPayments);
        Assert.NotNull(observation.Target);
        Assert.Equal(PaymentId, observation.Target.PaymentId);
        Assert.NotNull(store.Observation);
        Assert.Equal("tx-123", store.Observation.TransactionHash);
        Assert.Equal("0.00039980", store.Observation.ObservedAmount);
        Assert.Equal("test-provider", store.Observation.ProviderName);
    }

    [Fact]
    public async Task Poll_blockchain_observations_rejects_non_positive_batch_size()
    {
        var service = CreateService(new CapturingPaymentStore(), TimeSpan.FromHours(1));

        var exception = await Assert.ThrowsAsync<DomainRuleException>(() =>
            service.PollBlockchainObservationsAsync(0, CancellationToken.None));

        Assert.Equal("observation_batch_size.not_positive", exception.Code);
    }

    [Fact]
    public async Task Poll_blockchain_observations_isolates_one_project_failure_and_preserves_project_context()
    {
        var failingProjectId = Guid.NewGuid();
        var healthyProjectId = Guid.NewGuid();
        var store = new CapturingObservationStore();
        store.AddObservationTarget(new BlockchainObservationTarget(
            Guid.NewGuid(), "BTC", "failing-address", "0.1", failingProjectId));
        store.AddObservationTarget(new BlockchainObservationTarget(
            PaymentId, "BTC", "btc-test-address", "0.00039980", healthyProjectId));
        var service = CreateService(
            store,
            TimeSpan.FromHours(1),
            new FixedExchangeRateSource(),
            new FixedPaymentAddressProvider(),
            new ProjectSelectiveBlockchainObservationAdapter(failingProjectId));

        var result = await service.PollBlockchainObservationsAsync(25, CancellationToken.None);

        Assert.Equal(2, result.TargetCount);
        Assert.Equal(1, result.FailedCount);
        Assert.Equal(1, result.RecordedCount);
        Assert.Equal(healthyProjectId, store.Observation!.ProjectId);
    }

    [Fact]
    public async Task Monitor_blockchain_reorgs_updates_matching_transaction_confirmations()
    {
        var store = new CapturingReorgMonitoringStore();
        store.AddTarget(new BlockchainReorgMonitoringTarget(
            PaymentId,
            "BTC",
            "btc-test-address",
            "0.00039980",
            "tx-123",
            CurrentConfirmations: 1, ProjectId));
        var observation = new CapturingBlockchainObservationAdapter();
        observation.AddObservation(new BlockchainObservation(
            "tx-123",
            "0.00039980",
            DateTimeOffset.Parse("2026-07-04T12:05:00Z"),
            Confirmations: 0,
            "test-provider",
            "provider-observation-123"));
        var service = CreateService(
            store,
            TimeSpan.FromHours(1),
            new FixedExchangeRateSource(),
            new FixedPaymentAddressProvider(),
            observation);

        var result = await service.MonitorBlockchainReorgsAsync(25, CancellationToken.None);

        Assert.Equal(1, result.TargetCount);
        Assert.Equal(1, result.CheckedCount);
        Assert.Equal(0, result.UpdatedCount);
        Assert.Equal(1, result.ReorgAlertCount);
        Assert.Equal(0, result.MissingObservationCount);
        Assert.NotNull(store.MonitoringPolicy);
        Assert.Equal(1, store.MonitoringPolicy.BtcRequiredConfirmations);
        Assert.Equal(6, store.MonitoringPolicy.BtcMonitoringDepth);
        Assert.Equal(25, store.MaxReorgTransactions);
        Assert.NotNull(observation.Target);
        Assert.Equal(PaymentId, observation.Target.PaymentId);
        Assert.NotNull(store.ConfirmationUpdate);
        Assert.Equal("tx-123", store.ConfirmationUpdate.TransactionHash);
        Assert.Equal(0, store.ConfirmationUpdate.Confirmations);
        Assert.Equal("payment.reorg_alerted", store.ReorgPaymentEvent!.EventType);
    }

    [Fact]
    public async Task Monitor_blockchain_reorgs_isolates_a_failed_write_to_its_target()
    {
        var failingPaymentId = Guid.NewGuid();
        var store = new CapturingReorgMonitoringStore { FailingPaymentId = failingPaymentId };
        store.AddTarget(new BlockchainReorgMonitoringTarget(
            failingPaymentId, "BTC", "btc-test-address", "0.00039980", "tx-123", CurrentConfirmations: 1, ProjectId));
        store.AddTarget(new BlockchainReorgMonitoringTarget(
            PaymentId, "BTC", "btc-test-address", "0.00039980", "tx-123", CurrentConfirmations: 1, ProjectId));
        var observation = new CapturingBlockchainObservationAdapter();
        observation.AddObservation(new BlockchainObservation(
            "tx-123",
            "0.00039980",
            DateTimeOffset.Parse("2026-07-04T12:05:00Z"),
            Confirmations: 0,
            "test-provider",
            "provider-observation-123"));
        var service = CreateService(
            store,
            TimeSpan.FromHours(1),
            new FixedExchangeRateSource(),
            new FixedPaymentAddressProvider(),
            observation);

        var result = await service.MonitorBlockchainReorgsAsync(25, CancellationToken.None);

        Assert.Equal(2, result.TargetCount);
        Assert.Equal(1, result.FailedCount);
        Assert.Equal(1, result.ReorgAlertCount);
        Assert.Equal(PaymentId, store.ConfirmationUpdate!.PaymentId);
    }

    [Fact]
    public async Task Monitor_blockchain_reorgs_rejects_non_positive_batch_size()
    {
        var service = CreateService(new CapturingPaymentStore(), TimeSpan.FromHours(1));

        var exception = await Assert.ThrowsAsync<DomainRuleException>(() =>
            service.MonitorBlockchainReorgsAsync(0, CancellationToken.None));

        Assert.Equal("reorg_monitoring_batch_size.not_positive", exception.Code);
    }

    [Fact]
    public async Task Expire_due_payments_uses_clock_and_batch_size()
    {
        var store = new CapturingPaymentStore();
        var service = CreateService(store, TimeSpan.FromHours(1));

        var result = await service.ExpireDuePaymentsAsync(25, CancellationToken.None);

        Assert.Equal(2, result.ExpiredCount);
        Assert.Equal(DateTimeOffset.Parse("2026-07-04T12:00:00Z"), store.ExpiresBefore);
        Assert.Equal(25, store.MaxPayments);
    }

    [Fact]
    public async Task Update_blockchain_transaction_confirmations_records_confirmation_evidence()
    {
        var store = new CapturingConfirmationUpdateStore();
        var service = CreateService(store, TimeSpan.FromHours(1));

        var result = await service.UpdateBlockchainTransactionConfirmationsAsync(
            new UpdateBlockchainTransactionConfirmationsCommand(
                PaymentId,
                "btc",
                "tx-123",
                Confirmations: 1,
                BlockHash: "block-123",
                BlockHeight: 840000,
                ProjectId: ProjectId),
            CancellationToken.None);

        Assert.Equal(UpdateBlockchainTransactionConfirmationsResultKind.Completed, result.Kind);
        Assert.Equal("completed", result.Payment!.Status);
        Assert.Equal(DateTimeOffset.Parse("2026-07-04T12:00:00Z"), result.Payment.CompletedAt);
        Assert.NotNull(store.ConfirmationUpdate);
        Assert.Equal(PaymentId, store.ConfirmationUpdate.PaymentId);
        Assert.Equal("BTC", store.ConfirmationUpdate.SupportedCurrency);
        Assert.Equal("tx-123", store.ConfirmationUpdate.TransactionHash);
        Assert.Equal(1, store.ConfirmationUpdate.Confirmations);
        Assert.Equal("block-123", store.ConfirmationUpdate.BlockHash);
        Assert.Equal(840000, store.ConfirmationUpdate.BlockHeight);
        Assert.Equal(DateTimeOffset.Parse("2026-07-04T12:00:00Z"), store.ConfirmationUpdate.CheckedAt);
        Assert.Equal(1, store.CompletionPolicy!.RequiredConfirmations);
        Assert.Equal("payment.completed", store.CompletedPaymentEvent!.EventType);
        Assert.Equal("payment.completed", store.CompletedWebhookEvent!.EventType);
        Assert.Equal("payment.reorg_alerted", store.ReorgPaymentEvent!.EventType);
    }

    private static PaymentApplicationService CreateService(
        IPaymentStore paymentStore,
        TimeSpan paymentExpiration)
    {
        return CreateService(
            paymentStore,
            paymentExpiration,
            new FixedExchangeRateSource(),
            new FixedPaymentAddressProvider(),
            new CapturingBlockchainObservationAdapter());
    }

    private static PaymentApplicationService CreateService(
        IPaymentStore paymentStore,
        TimeSpan paymentExpiration,
        IExchangeRateSource exchangeRateSource,
        IPaymentAddressProvider paymentAddressProvider,
        IBlockchainObservationAdapter blockchainObservationAdapter)
    {
        return new PaymentApplicationService(
            paymentStore,
            new FixedPayerPageIdGenerator(),
            exchangeRateSource,
            paymentAddressProvider,
            new FixedProjectPaymentConfigurationStore(paymentExpiration),
            blockchainObservationAdapter,
            new FixedClock(),
            Options.Create(new PaymentApplicationOptions
            {
                PayerPageBaseUrl = "https://pay.example.test/pay",
                PaymentExpiration = paymentExpiration,
                LateAcceptanceWindow = TimeSpan.FromHours(24),
            }));
    }

    private sealed class FixedProjectPaymentConfigurationStore(TimeSpan paymentExpiration)
        : IProjectPaymentConfigurationStore
    {
        private readonly ProjectPaymentConfiguration _configuration = new(
            Guid.Parse("00000000-0000-0000-0000-000000000001"),
            "active",
            paymentExpiration,
            TimeSpan.FromHours(24),
            1m,
            new ProjectCurrencyConfiguration(true, 1, 6),
            new ProjectCurrencyConfiguration(true, 1, 12),
            new ProjectCurrencyConfiguration(true, 12, 64));

        public Task<ProjectPaymentConfiguration?> FindByCredentialAsync(
            Guid integrationApiCredentialId,
            CancellationToken cancellationToken) => Task.FromResult<ProjectPaymentConfiguration?>(_configuration);

        public Task<ProjectPaymentConfiguration?> FindByProjectAsync(
            Guid projectId,
            CancellationToken cancellationToken) => Task.FromResult<ProjectPaymentConfiguration?>(_configuration);
    }

    private sealed class CapturingPaymentStore : IPaymentStore
    {
        public string? RequestHash { get; private set; }

        public IReadOnlyList<PaymentOptionDraft> PaymentOptions { get; private set; } = [];

        public DateTimeOffset? ExpiresBefore { get; private set; }

        public int? MaxPayments { get; private set; }

        public Task<CreatePaymentStoreResult> CreateAsync(
            Guid integrationApiCredentialId,
            string idempotencyKey,
            string requestHash,
            PaymentDraft payment,
            IReadOnlyCollection<PaymentOptionDraft> paymentOptions,
            IReadOnlyCollection<PaymentEventDraft> paymentEvents,
            IReadOnlyCollection<WebhookOutboxEventDraft> webhookEvents,
            CancellationToken cancellationToken)
        {
            RequestHash = requestHash;
            PaymentOptions = paymentOptions.ToArray();
            return Task.FromResult(CreatePaymentStoreResult.Created(new PaymentReadModel(
                payment.Id,
                integrationApiCredentialId,
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
                payment.UpdatedAt)));
        }

        public Task<PaymentReadModel?> FindAsync(
            Guid integrationApiCredentialId,
            Guid paymentId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<PaymentReadModel?>(null);
        }

        public Task<PaymentReadModel?> FindByPayerPageIdAsync(
            string payerPageId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<PaymentReadModel?>(null);
        }

        public Task<IReadOnlyList<BlockchainObservationTarget>> ListBlockchainObservationTargetsAsync(
            DateTimeOffset observedUntil,
            TimeSpan observedConfirmationWait,
            int maxPayments,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<BlockchainObservationTarget>>([]);
        }

        public Task<IReadOnlyList<BlockchainReorgMonitoringTarget>> ListBlockchainReorgMonitoringTargetsAsync(
            ReorgMonitoringPolicyDraft monitoringPolicy,
            int maxTransactions,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<BlockchainReorgMonitoringTarget>>([]);
        }

        public Task<SelectCurrencyStoreResult> SelectCurrencyAsync(
            PaymentSelectionDraft selection,
            PaymentEventDraft paymentEvent,
            WebhookOutboxEventDraft webhookEvent,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(SelectCurrencyStoreResult.NotFound());
        }

        public Task<RecordBlockchainObservationStoreResult> RecordBlockchainObservationAsync(
            BlockchainObservationDraft observation,
            PaymentCompletionPolicyDraft completionPolicy,
            PaymentEventDraft paymentEvent,
            WebhookOutboxEventDraft webhookEvent,
            PaymentEventDraft completedPaymentEvent,
            WebhookOutboxEventDraft completedWebhookEvent,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(RecordBlockchainObservationStoreResult.PaymentNotFound());
        }

        public Task<ExpireDuePaymentsStoreResult> ExpireDuePaymentsAsync(
            DateTimeOffset expiresBefore,
            TimeSpan observedConfirmationWait,
            int maxPayments,
            CancellationToken cancellationToken)
        {
            ExpiresBefore = expiresBefore;
            MaxPayments = maxPayments;
            return Task.FromResult(new ExpireDuePaymentsStoreResult(2));
        }

        public Task<UpdateBlockchainTransactionConfirmationsStoreResult> RecordMissingBlockchainTransactionAsync(
            BlockchainTransactionMissingDraft missingTransaction,
            PaymentEventDraft reorgPaymentEvent,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(UpdateBlockchainTransactionConfirmationsStoreResult.TransactionNotFound());
        }

        public Task<UpdateBlockchainTransactionConfirmationsStoreResult> UpdateBlockchainTransactionConfirmationsAsync(
            BlockchainTransactionConfirmationUpdateDraft confirmationUpdate,
            PaymentCompletionPolicyDraft completionPolicy,
            PaymentEventDraft completedPaymentEvent,
            WebhookOutboxEventDraft completedWebhookEvent,
            PaymentEventDraft reorgPaymentEvent,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(UpdateBlockchainTransactionConfirmationsStoreResult.PaymentNotFound());
        }
    }

    private sealed class CapturingSelectionStore(
        bool optionAvailable = true,
        string unavailableReason = PaymentOptionUnavailableReasons.ExchangeRate) : IPaymentStore
    {
        public PaymentSelectionDraft? Selection { get; private set; }

        public PaymentEventDraft? PaymentEvent { get; private set; }

        public WebhookOutboxEventDraft? WebhookEvent { get; private set; }

        public Task<CreatePaymentStoreResult> CreateAsync(
            Guid integrationApiCredentialId,
            string idempotencyKey,
            string requestHash,
            PaymentDraft payment,
            IReadOnlyCollection<PaymentOptionDraft> paymentOptions,
            IReadOnlyCollection<PaymentEventDraft> paymentEvents,
            IReadOnlyCollection<WebhookOutboxEventDraft> webhookEvents,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<PaymentReadModel?> FindAsync(
            Guid integrationApiCredentialId,
            Guid paymentId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<PaymentReadModel?>(null);
        }

        public Task<PaymentReadModel?> FindByPayerPageIdAsync(
            string payerPageId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<PaymentReadModel?>(new PaymentReadModel(
                PaymentId,
                CredentialId,
                "order-123",
                "EUR",
                1999,
                "pending_currency_selection",
                payerPageId,
                DateTimeOffset.Parse("2026-07-04T13:00:00Z"),
                DateTimeOffset.Parse("2026-07-05T13:00:00Z"),
                ContextUsername: null,
                ContextCustomerNumber: null,
                ContextCartName: null,
                ContextNote: null,
                ReturnUrl: null,
                SelectedCurrency: null,
                ExpectedCryptoAmount: null,
                PaymentAddress: null,
                ObservedTotal: null,
                ConfirmedEligibleTotal: null,
                CompletedAt: null,
                DateTimeOffset.Parse("2026-07-04T12:00:00Z"),
                DateTimeOffset.Parse("2026-07-04T12:00:00Z"),
                PaymentOptions:
                [
                    new PaymentOptionReadModel(
                        "BTC",
                        optionAvailable ? "available" : "unavailable",
                        optionAvailable ? null : unavailableReason),
                ]));
        }

        public Task<IReadOnlyList<BlockchainObservationTarget>> ListBlockchainObservationTargetsAsync(
            DateTimeOffset observedUntil,
            TimeSpan observedConfirmationWait,
            int maxPayments,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<BlockchainObservationTarget>>([]);
        }

        public Task<IReadOnlyList<BlockchainReorgMonitoringTarget>> ListBlockchainReorgMonitoringTargetsAsync(
            ReorgMonitoringPolicyDraft monitoringPolicy,
            int maxTransactions,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<BlockchainReorgMonitoringTarget>>([]);
        }

        public Task<SelectCurrencyStoreResult> SelectCurrencyAsync(
            PaymentSelectionDraft selection,
            PaymentEventDraft paymentEvent,
            WebhookOutboxEventDraft webhookEvent,
            CancellationToken cancellationToken)
        {
            Selection = selection;
            PaymentEvent = paymentEvent;
            WebhookEvent = webhookEvent;

            return Task.FromResult(SelectCurrencyStoreResult.Selected(new PaymentReadModel(
                PaymentId,
                CredentialId,
                "order-123",
                "EUR",
                1999,
                "waiting_for_payment",
                "payer-page-id",
                DateTimeOffset.Parse("2026-07-04T13:00:00Z"),
                DateTimeOffset.Parse("2026-07-05T13:00:00Z"),
                ContextUsername: null,
                ContextCustomerNumber: null,
                ContextCartName: null,
                ContextNote: null,
                ReturnUrl: null,
                selection.SupportedCurrency,
                selection.ExpectedCryptoAmount,
                selection.PaymentAddress,
                ObservedTotal: null,
                ConfirmedEligibleTotal: null,
                CompletedAt: null,
                DateTimeOffset.Parse("2026-07-04T12:00:00Z"),
                selection.SelectedAt)));
        }

        public Task<RecordBlockchainObservationStoreResult> RecordBlockchainObservationAsync(
            BlockchainObservationDraft observation,
            PaymentCompletionPolicyDraft completionPolicy,
            PaymentEventDraft paymentEvent,
            WebhookOutboxEventDraft webhookEvent,
            PaymentEventDraft completedPaymentEvent,
            WebhookOutboxEventDraft completedWebhookEvent,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(RecordBlockchainObservationStoreResult.PaymentNotReady());
        }

        public Task<ExpireDuePaymentsStoreResult> ExpireDuePaymentsAsync(
            DateTimeOffset expiresBefore,
            TimeSpan observedConfirmationWait,
            int maxPayments,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new ExpireDuePaymentsStoreResult(0));
        }

        public Task<UpdateBlockchainTransactionConfirmationsStoreResult> RecordMissingBlockchainTransactionAsync(
            BlockchainTransactionMissingDraft missingTransaction,
            PaymentEventDraft reorgPaymentEvent,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(UpdateBlockchainTransactionConfirmationsStoreResult.TransactionNotFound());
        }

        public Task<UpdateBlockchainTransactionConfirmationsStoreResult> UpdateBlockchainTransactionConfirmationsAsync(
            BlockchainTransactionConfirmationUpdateDraft confirmationUpdate,
            PaymentCompletionPolicyDraft completionPolicy,
            PaymentEventDraft completedPaymentEvent,
            WebhookOutboxEventDraft completedWebhookEvent,
            PaymentEventDraft reorgPaymentEvent,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(UpdateBlockchainTransactionConfirmationsStoreResult.PaymentNotReady());
        }
    }

    private sealed class CapturingObservationStore(bool completePayment = false) : IPaymentStore
    {
        private readonly List<BlockchainObservationTarget> _observationTargets = [];

        public BlockchainObservationDraft? Observation { get; private set; }

        public PaymentCompletionPolicyDraft? CompletionPolicy { get; private set; }

        public PaymentEventDraft? PaymentEvent { get; private set; }

        public WebhookOutboxEventDraft? WebhookEvent { get; private set; }

        public PaymentEventDraft? CompletedPaymentEvent { get; private set; }

        public WebhookOutboxEventDraft? CompletedWebhookEvent { get; private set; }

        public DateTimeOffset? ObservedUntil { get; private set; }

        public int? MaxObservationPayments { get; private set; }

        public void AddObservationTarget(BlockchainObservationTarget target)
        {
            _observationTargets.Add(target);
        }

        public Task<CreatePaymentStoreResult> CreateAsync(
            Guid integrationApiCredentialId,
            string idempotencyKey,
            string requestHash,
            PaymentDraft payment,
            IReadOnlyCollection<PaymentOptionDraft> paymentOptions,
            IReadOnlyCollection<PaymentEventDraft> paymentEvents,
            IReadOnlyCollection<WebhookOutboxEventDraft> webhookEvents,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<PaymentReadModel?> FindAsync(
            Guid integrationApiCredentialId,
            Guid paymentId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<PaymentReadModel?>(null);
        }

        public Task<PaymentReadModel?> FindByPayerPageIdAsync(
            string payerPageId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<PaymentReadModel?>(null);
        }

        public Task<IReadOnlyList<BlockchainObservationTarget>> ListBlockchainObservationTargetsAsync(
            DateTimeOffset observedUntil,
            TimeSpan observedConfirmationWait,
            int maxPayments,
            CancellationToken cancellationToken)
        {
            ObservedUntil = observedUntil;
            MaxObservationPayments = maxPayments;
            return Task.FromResult<IReadOnlyList<BlockchainObservationTarget>>(_observationTargets);
        }

        public Task<IReadOnlyList<BlockchainReorgMonitoringTarget>> ListBlockchainReorgMonitoringTargetsAsync(
            ReorgMonitoringPolicyDraft monitoringPolicy,
            int maxTransactions,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<BlockchainReorgMonitoringTarget>>([]);
        }

        public Task<SelectCurrencyStoreResult> SelectCurrencyAsync(
            PaymentSelectionDraft selection,
            PaymentEventDraft paymentEvent,
            WebhookOutboxEventDraft webhookEvent,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<RecordBlockchainObservationStoreResult> RecordBlockchainObservationAsync(
            BlockchainObservationDraft observation,
            PaymentCompletionPolicyDraft completionPolicy,
            PaymentEventDraft paymentEvent,
            WebhookOutboxEventDraft webhookEvent,
            PaymentEventDraft completedPaymentEvent,
            WebhookOutboxEventDraft completedWebhookEvent,
            CancellationToken cancellationToken)
        {
            Observation = observation;
            CompletionPolicy = completionPolicy;
            PaymentEvent = paymentEvent;
            WebhookEvent = webhookEvent;
            CompletedPaymentEvent = completedPaymentEvent;
            CompletedWebhookEvent = completedWebhookEvent;

            var payment = new PaymentReadModel(
                PaymentId,
                CredentialId,
                "order-123",
                "EUR",
                1999,
                completePayment ? "completed" : "observed",
                "payer-page-id",
                DateTimeOffset.Parse("2026-07-04T13:00:00Z"),
                DateTimeOffset.Parse("2026-07-05T13:00:00Z"),
                ContextUsername: null,
                ContextCustomerNumber: null,
                ContextCartName: null,
                ContextNote: null,
                ReturnUrl: null,
                "BTC",
                "0.00039980",
                "btc-test-address",
                "0.0003998",
                completePayment ? "0.0003998" : null,
                completePayment ? observation.UpdatedAt : null,
                DateTimeOffset.Parse("2026-07-04T12:00:00Z"),
                observation.UpdatedAt);

            return Task.FromResult(completePayment
                ? RecordBlockchainObservationStoreResult.Completed(payment)
                : RecordBlockchainObservationStoreResult.Observed(payment));
        }

        public Task<ExpireDuePaymentsStoreResult> ExpireDuePaymentsAsync(
            DateTimeOffset expiresBefore,
            TimeSpan observedConfirmationWait,
            int maxPayments,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new ExpireDuePaymentsStoreResult(0));
        }

        public Task<UpdateBlockchainTransactionConfirmationsStoreResult> RecordMissingBlockchainTransactionAsync(
            BlockchainTransactionMissingDraft missingTransaction,
            PaymentEventDraft reorgPaymentEvent,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(UpdateBlockchainTransactionConfirmationsStoreResult.TransactionNotFound());
        }

        public Task<UpdateBlockchainTransactionConfirmationsStoreResult> UpdateBlockchainTransactionConfirmationsAsync(
            BlockchainTransactionConfirmationUpdateDraft confirmationUpdate,
            PaymentCompletionPolicyDraft completionPolicy,
            PaymentEventDraft completedPaymentEvent,
            WebhookOutboxEventDraft completedWebhookEvent,
            PaymentEventDraft reorgPaymentEvent,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(UpdateBlockchainTransactionConfirmationsStoreResult.TransactionNotFound());
        }
    }

    private sealed class CapturingConfirmationUpdateStore : IPaymentStore
    {
        public BlockchainTransactionConfirmationUpdateDraft? ConfirmationUpdate { get; private set; }

        public PaymentCompletionPolicyDraft? CompletionPolicy { get; private set; }

        public PaymentEventDraft? CompletedPaymentEvent { get; private set; }

        public WebhookOutboxEventDraft? CompletedWebhookEvent { get; private set; }

        public PaymentEventDraft? ReorgPaymentEvent { get; private set; }

        public Task<CreatePaymentStoreResult> CreateAsync(
            Guid integrationApiCredentialId,
            string idempotencyKey,
            string requestHash,
            PaymentDraft payment,
            IReadOnlyCollection<PaymentOptionDraft> paymentOptions,
            IReadOnlyCollection<PaymentEventDraft> paymentEvents,
            IReadOnlyCollection<WebhookOutboxEventDraft> webhookEvents,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<PaymentReadModel?> FindAsync(
            Guid integrationApiCredentialId,
            Guid paymentId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<PaymentReadModel?>(null);
        }

        public Task<PaymentReadModel?> FindByPayerPageIdAsync(
            string payerPageId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<PaymentReadModel?>(null);
        }

        public Task<IReadOnlyList<BlockchainObservationTarget>> ListBlockchainObservationTargetsAsync(
            DateTimeOffset observedUntil,
            TimeSpan observedConfirmationWait,
            int maxPayments,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<BlockchainObservationTarget>>([]);
        }

        public Task<IReadOnlyList<BlockchainReorgMonitoringTarget>> ListBlockchainReorgMonitoringTargetsAsync(
            ReorgMonitoringPolicyDraft monitoringPolicy,
            int maxTransactions,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<BlockchainReorgMonitoringTarget>>([]);
        }

        public Task<SelectCurrencyStoreResult> SelectCurrencyAsync(
            PaymentSelectionDraft selection,
            PaymentEventDraft paymentEvent,
            WebhookOutboxEventDraft webhookEvent,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<RecordBlockchainObservationStoreResult> RecordBlockchainObservationAsync(
            BlockchainObservationDraft observation,
            PaymentCompletionPolicyDraft completionPolicy,
            PaymentEventDraft paymentEvent,
            WebhookOutboxEventDraft webhookEvent,
            PaymentEventDraft completedPaymentEvent,
            WebhookOutboxEventDraft completedWebhookEvent,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<ExpireDuePaymentsStoreResult> ExpireDuePaymentsAsync(
            DateTimeOffset expiresBefore,
            TimeSpan observedConfirmationWait,
            int maxPayments,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<UpdateBlockchainTransactionConfirmationsStoreResult> RecordMissingBlockchainTransactionAsync(
            BlockchainTransactionMissingDraft missingTransaction,
            PaymentEventDraft reorgPaymentEvent,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(UpdateBlockchainTransactionConfirmationsStoreResult.TransactionNotFound());
        }

        public Task<UpdateBlockchainTransactionConfirmationsStoreResult> UpdateBlockchainTransactionConfirmationsAsync(
            BlockchainTransactionConfirmationUpdateDraft confirmationUpdate,
            PaymentCompletionPolicyDraft completionPolicy,
            PaymentEventDraft completedPaymentEvent,
            WebhookOutboxEventDraft completedWebhookEvent,
            PaymentEventDraft reorgPaymentEvent,
            CancellationToken cancellationToken)
        {
            ConfirmationUpdate = confirmationUpdate;
            CompletionPolicy = completionPolicy;
            CompletedPaymentEvent = completedPaymentEvent;
            CompletedWebhookEvent = completedWebhookEvent;
            ReorgPaymentEvent = reorgPaymentEvent;

            return Task.FromResult(UpdateBlockchainTransactionConfirmationsStoreResult.Completed(new PaymentReadModel(
                PaymentId,
                CredentialId,
                "order-123",
                "EUR",
                1999,
                "completed",
                "payer-page-id",
                DateTimeOffset.Parse("2026-07-04T13:00:00Z"),
                DateTimeOffset.Parse("2026-07-05T13:00:00Z"),
                ContextUsername: null,
                ContextCustomerNumber: null,
                ContextCartName: null,
                ContextNote: null,
                ReturnUrl: null,
                "BTC",
                "0.00039980",
                "btc-test-address",
                "0.0003998",
                "0.0003998",
                confirmationUpdate.CheckedAt,
                DateTimeOffset.Parse("2026-07-04T12:00:00Z"),
                confirmationUpdate.CheckedAt)));
        }
    }

    private sealed class CapturingReorgMonitoringStore : IPaymentStore
    {
        private readonly List<BlockchainReorgMonitoringTarget> _targets = [];

        public ReorgMonitoringPolicyDraft? MonitoringPolicy { get; private set; }

        public int MaxReorgTransactions { get; private set; }

        public BlockchainTransactionConfirmationUpdateDraft? ConfirmationUpdate { get; private set; }

        public PaymentEventDraft? ReorgPaymentEvent { get; private set; }

        public Guid? FailingPaymentId { get; init; }

        public void AddTarget(BlockchainReorgMonitoringTarget target)
        {
            _targets.Add(target);
        }

        public Task<CreatePaymentStoreResult> CreateAsync(
            Guid integrationApiCredentialId,
            string idempotencyKey,
            string requestHash,
            PaymentDraft payment,
            IReadOnlyCollection<PaymentOptionDraft> paymentOptions,
            IReadOnlyCollection<PaymentEventDraft> paymentEvents,
            IReadOnlyCollection<WebhookOutboxEventDraft> webhookEvents,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<PaymentReadModel?> FindAsync(
            Guid integrationApiCredentialId,
            Guid paymentId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<PaymentReadModel?>(null);
        }

        public Task<PaymentReadModel?> FindByPayerPageIdAsync(
            string payerPageId,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<PaymentReadModel?>(null);
        }

        public Task<IReadOnlyList<BlockchainObservationTarget>> ListBlockchainObservationTargetsAsync(
            DateTimeOffset observedUntil,
            TimeSpan observedConfirmationWait,
            int maxPayments,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<BlockchainObservationTarget>>([]);
        }

        public Task<IReadOnlyList<BlockchainReorgMonitoringTarget>> ListBlockchainReorgMonitoringTargetsAsync(
            ReorgMonitoringPolicyDraft monitoringPolicy,
            int maxTransactions,
            CancellationToken cancellationToken)
        {
            MonitoringPolicy = monitoringPolicy;
            MaxReorgTransactions = maxTransactions;
            return Task.FromResult<IReadOnlyList<BlockchainReorgMonitoringTarget>>(_targets);
        }

        public Task<SelectCurrencyStoreResult> SelectCurrencyAsync(
            PaymentSelectionDraft selection,
            PaymentEventDraft paymentEvent,
            WebhookOutboxEventDraft webhookEvent,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<RecordBlockchainObservationStoreResult> RecordBlockchainObservationAsync(
            BlockchainObservationDraft observation,
            PaymentCompletionPolicyDraft completionPolicy,
            PaymentEventDraft paymentEvent,
            WebhookOutboxEventDraft webhookEvent,
            PaymentEventDraft completedPaymentEvent,
            WebhookOutboxEventDraft completedWebhookEvent,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<ExpireDuePaymentsStoreResult> ExpireDuePaymentsAsync(
            DateTimeOffset expiresBefore,
            TimeSpan observedConfirmationWait,
            int maxPayments,
            CancellationToken cancellationToken)
        {
            throw new NotSupportedException();
        }

        public Task<UpdateBlockchainTransactionConfirmationsStoreResult> RecordMissingBlockchainTransactionAsync(
            BlockchainTransactionMissingDraft missingTransaction,
            PaymentEventDraft reorgPaymentEvent,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(UpdateBlockchainTransactionConfirmationsStoreResult.TransactionNotFound());
        }

        public Task<UpdateBlockchainTransactionConfirmationsStoreResult> UpdateBlockchainTransactionConfirmationsAsync(
            BlockchainTransactionConfirmationUpdateDraft confirmationUpdate,
            PaymentCompletionPolicyDraft completionPolicy,
            PaymentEventDraft completedPaymentEvent,
            WebhookOutboxEventDraft completedWebhookEvent,
            PaymentEventDraft reorgPaymentEvent,
            CancellationToken cancellationToken)
        {
            if (confirmationUpdate.PaymentId == FailingPaymentId)
            {
                throw new InvalidOperationException("Simulated write failure.");
            }

            ConfirmationUpdate = confirmationUpdate;
            ReorgPaymentEvent = reorgPaymentEvent;

            return Task.FromResult(UpdateBlockchainTransactionConfirmationsStoreResult.ReorgAlerted(new PaymentReadModel(
                PaymentId,
                CredentialId,
                "order-123",
                "EUR",
                1999,
                "completed",
                "payer-page-id",
                DateTimeOffset.Parse("2026-07-04T13:00:00Z"),
                DateTimeOffset.Parse("2026-07-05T13:00:00Z"),
                ContextUsername: null,
                ContextCustomerNumber: null,
                ContextCartName: null,
                ContextNote: null,
                ReturnUrl: null,
                "BTC",
                "0.00039980",
                "btc-test-address",
                "0.0003998",
                "0.0003998",
                confirmationUpdate.CheckedAt,
                DateTimeOffset.Parse("2026-07-04T12:00:00Z"),
                confirmationUpdate.CheckedAt)));
        }
    }

    private sealed class FixedPayerPageIdGenerator : IPayerPageIdGenerator
    {
        public string Generate() => "fixed-payer-page-id";
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow { get; } = DateTimeOffset.Parse("2026-07-04T12:00:00Z");
    }

    private sealed class FixedExchangeRateSource : IExchangeRateSource
    {
        public Task<RateLockQuote?> GetRateLockQuoteAsync(
            string fiatCurrency,
            long fiatAmountMinor,
            string supportedCurrency,
            DateTimeOffset requestedAt,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<RateLockQuote?>(new RateLockQuote(
                supportedCurrency,
                "test-rate-source",
                "50000.00",
                "0.00039980",
                requestedAt));
        }
    }

    private sealed class SelectiveExchangeRateSource(params string[] availableCurrencies)
        : IExchangeRateSource
    {
        private readonly HashSet<string> _availableCurrencies =
            availableCurrencies.ToHashSet(StringComparer.Ordinal);

        public Task<RateLockQuote?> GetRateLockQuoteAsync(
            string fiatCurrency,
            long fiatAmountMinor,
            string supportedCurrency,
            DateTimeOffset requestedAt,
            CancellationToken cancellationToken) =>
            Task.FromResult<RateLockQuote?>(null);

        public Task<bool> IsRateAvailableAsync(
            string fiatCurrency,
            string supportedCurrency,
            DateTimeOffset checkedAt,
            CancellationToken cancellationToken) =>
            Task.FromResult(_availableCurrencies.Contains(supportedCurrency));
    }

    private sealed class FixedPaymentAddressProvider : IPaymentAddressProvider
    {
        public Task<PaymentAddressAssignment?> AssignAsync(
            Guid projectId,
            Guid paymentId,
            string supportedCurrency,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<PaymentAddressAssignment?>(new PaymentAddressAssignment(
                supportedCurrency,
                $"{supportedCurrency.ToLowerInvariant()}-test-address"));
        }
    }

    private sealed class SelectivePaymentAddressProvider(params string[] availableCurrencies)
        : IPaymentAddressProvider
    {
        private readonly HashSet<string> _availableCurrencies =
            availableCurrencies.ToHashSet(StringComparer.Ordinal);

        public Task<PaymentAddressAssignment?> AssignAsync(
            Guid projectId,
            Guid paymentId,
            string supportedCurrency,
            CancellationToken cancellationToken) =>
            Task.FromResult<PaymentAddressAssignment?>(null);

        public Task<bool> IsAddressAvailableAsync(
            Guid projectId,
            string supportedCurrency,
            CancellationToken cancellationToken) =>
            Task.FromResult(_availableCurrencies.Contains(supportedCurrency));
    }

    private sealed class CapturingBlockchainObservationAdapter : IBlockchainObservationAdapter
    {
        private readonly List<BlockchainObservation> _observations = [];

        public BlockchainObservationTarget? Target { get; private set; }

        public void AddObservation(BlockchainObservation observation)
        {
            _observations.Add(observation);
        }

        public Task StartWatchingAsync(
            BlockchainObservationTarget target,
            CancellationToken cancellationToken)
        {
            Target = target;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<BlockchainObservation>> PollAsync(
            BlockchainObservationTarget target,
            CancellationToken cancellationToken)
        {
            Target = target;
            return Task.FromResult<IReadOnlyList<BlockchainObservation>>(_observations);
        }
    }

    private sealed class ProjectSelectiveBlockchainObservationAdapter(Guid failingProjectId)
        : IBlockchainObservationAdapter
    {
        public Task StartWatchingAsync(
            BlockchainObservationTarget target,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<IReadOnlyList<BlockchainObservation>> PollAsync(
            BlockchainObservationTarget target,
            CancellationToken cancellationToken)
        {
            if (target.ProjectId == failingProjectId)
            {
                throw new HttpRequestException("Simulated provider failure.");
            }

            return Task.FromResult<IReadOnlyList<BlockchainObservation>>(
            [
                new BlockchainObservation(
                    "tx-healthy",
                    "0.00039980",
                    DateTimeOffset.Parse("2026-07-04T12:05:00Z"),
                    0,
                    "test-provider",
                    "healthy-observation"),
            ]);
        }
    }

    private sealed class FailingWatchAdapter : IBlockchainObservationAdapter
    {
        public Task StartWatchingAsync(
            BlockchainObservationTarget target,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Provider API key reference could not be resolved.");

        public Task<IReadOnlyList<BlockchainObservation>> PollAsync(
            BlockchainObservationTarget target,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<BlockchainObservation>>([]);
    }

    private sealed class SelectiveBlockchainObservationAdapter(
        params string[] availableCurrencies) : IBlockchainObservationAdapter
    {
        private readonly HashSet<string> _availableCurrencies =
            availableCurrencies.ToHashSet(StringComparer.Ordinal);

        public Task StartWatchingAsync(
            BlockchainObservationTarget target,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<BlockchainObservation>> PollAsync(
            BlockchainObservationTarget target,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<BlockchainObservation>>([]);

        public Task<bool> IsObservationAvailableAsync(
            string supportedCurrency,
            CancellationToken cancellationToken) =>
            Task.FromResult(_availableCurrencies.Contains(supportedCurrency));
    }
}
