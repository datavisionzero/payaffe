namespace Payaffe.Application.Payments;

public interface IPaymentStore
{
    Task<CreatePaymentStoreResult> CreateAsync(
        Guid integrationApiCredentialId,
        string idempotencyKey,
        string requestHash,
        PaymentDraft payment,
        IReadOnlyCollection<PaymentOptionDraft> paymentOptions,
        IReadOnlyCollection<PaymentEventDraft> paymentEvents,
        IReadOnlyCollection<WebhookOutboxEventDraft> webhookEvents,
        CancellationToken cancellationToken);

    Task<PaymentReadModel?> FindAsync(
        Guid integrationApiCredentialId,
        Guid paymentId,
        CancellationToken cancellationToken);

    Task<PaymentReadModel?> FindByPayerPageIdAsync(
        string payerPageId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<BlockchainObservationTarget>> ListBlockchainObservationTargetsAsync(
        DateTimeOffset observedUntil,
        int maxPayments,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<BlockchainReorgMonitoringTarget>> ListBlockchainReorgMonitoringTargetsAsync(
        ReorgMonitoringPolicyDraft monitoringPolicy,
        int maxTransactions,
        CancellationToken cancellationToken);

    Task<SelectCurrencyStoreResult> SelectCurrencyAsync(
        PaymentSelectionDraft selection,
        PaymentEventDraft paymentEvent,
        WebhookOutboxEventDraft webhookEvent,
        CancellationToken cancellationToken);

    Task<RecordBlockchainObservationStoreResult> RecordBlockchainObservationAsync(
        BlockchainObservationDraft observation,
        PaymentCompletionPolicyDraft completionPolicy,
        PaymentEventDraft paymentEvent,
        WebhookOutboxEventDraft webhookEvent,
        PaymentEventDraft completedPaymentEvent,
        WebhookOutboxEventDraft completedWebhookEvent,
        CancellationToken cancellationToken);

    Task<ExpireDuePaymentsStoreResult> ExpireDuePaymentsAsync(
        DateTimeOffset expiresBefore,
        int maxPayments,
        CancellationToken cancellationToken);

    Task<UpdateBlockchainTransactionConfirmationsStoreResult> UpdateBlockchainTransactionConfirmationsAsync(
        BlockchainTransactionConfirmationUpdateDraft confirmationUpdate,
        PaymentCompletionPolicyDraft completionPolicy,
        PaymentEventDraft completedPaymentEvent,
        WebhookOutboxEventDraft completedWebhookEvent,
        PaymentEventDraft reorgPaymentEvent,
        CancellationToken cancellationToken);
}

public sealed record CreatePaymentStoreResult(
    CreatePaymentStoreResultKind Kind,
    PaymentReadModel? Payment)
{
    public static CreatePaymentStoreResult Created(PaymentReadModel payment) =>
        new(CreatePaymentStoreResultKind.Created, payment);

    public static CreatePaymentStoreResult Existing(PaymentReadModel payment) =>
        new(CreatePaymentStoreResultKind.Existing, payment);

    public static CreatePaymentStoreResult IdempotencyConflict() =>
        new(CreatePaymentStoreResultKind.IdempotencyConflict, Payment: null);

    public static CreatePaymentStoreResult ProjectUnavailable() =>
        new(CreatePaymentStoreResultKind.ProjectUnavailable, Payment: null);
}

public enum CreatePaymentStoreResultKind
{
    Created,
    Existing,
    IdempotencyConflict,
    ProjectUnavailable,
}

public sealed record SelectCurrencyStoreResult(
    SelectCurrencyStoreResultKind Kind,
    PaymentReadModel? Payment)
{
    public static SelectCurrencyStoreResult Selected(PaymentReadModel payment) =>
        new(SelectCurrencyStoreResultKind.Selected, payment);

    public static SelectCurrencyStoreResult AlreadySelected(PaymentReadModel payment) =>
        new(SelectCurrencyStoreResultKind.AlreadySelected, payment);

    public static SelectCurrencyStoreResult NotFound() =>
        new(SelectCurrencyStoreResultKind.NotFound, Payment: null);
}

public enum SelectCurrencyStoreResultKind
{
    Selected,
    AlreadySelected,
    NotFound,
}

public sealed record RecordBlockchainObservationStoreResult(
    RecordBlockchainObservationStoreResultKind Kind,
    PaymentReadModel? Payment)
{
    public static RecordBlockchainObservationStoreResult Observed(PaymentReadModel payment) =>
        new(RecordBlockchainObservationStoreResultKind.Observed, payment);

    public static RecordBlockchainObservationStoreResult Completed(PaymentReadModel payment) =>
        new(RecordBlockchainObservationStoreResultKind.Completed, payment);

    public static RecordBlockchainObservationStoreResult AlreadyRecorded(PaymentReadModel payment) =>
        new(RecordBlockchainObservationStoreResultKind.AlreadyRecorded, payment);

    public static RecordBlockchainObservationStoreResult PaymentNotFound() =>
        new(RecordBlockchainObservationStoreResultKind.PaymentNotFound, Payment: null);

    public static RecordBlockchainObservationStoreResult PaymentNotReady() =>
        new(RecordBlockchainObservationStoreResultKind.PaymentNotReady, Payment: null);

    public static RecordBlockchainObservationStoreResult ObservationMismatch() =>
        new(RecordBlockchainObservationStoreResultKind.ObservationMismatch, Payment: null);
}

public enum RecordBlockchainObservationStoreResultKind
{
    Observed,
    Completed,
    AlreadyRecorded,
    PaymentNotFound,
    PaymentNotReady,
    ObservationMismatch,
}

public sealed record ExpireDuePaymentsStoreResult(int ExpiredCount);

public sealed record UpdateBlockchainTransactionConfirmationsStoreResult(
    UpdateBlockchainTransactionConfirmationsStoreResultKind Kind,
    PaymentReadModel? Payment)
{
    public static UpdateBlockchainTransactionConfirmationsStoreResult Updated(PaymentReadModel payment) =>
        new(UpdateBlockchainTransactionConfirmationsStoreResultKind.Updated, payment);

    public static UpdateBlockchainTransactionConfirmationsStoreResult Completed(PaymentReadModel payment) =>
        new(UpdateBlockchainTransactionConfirmationsStoreResultKind.Completed, payment);

    public static UpdateBlockchainTransactionConfirmationsStoreResult ReorgAlerted(PaymentReadModel payment) =>
        new(UpdateBlockchainTransactionConfirmationsStoreResultKind.ReorgAlerted, payment);

    public static UpdateBlockchainTransactionConfirmationsStoreResult PaymentNotFound() =>
        new(UpdateBlockchainTransactionConfirmationsStoreResultKind.PaymentNotFound, Payment: null);

    public static UpdateBlockchainTransactionConfirmationsStoreResult PaymentNotReady() =>
        new(UpdateBlockchainTransactionConfirmationsStoreResultKind.PaymentNotReady, Payment: null);

    public static UpdateBlockchainTransactionConfirmationsStoreResult TransactionNotFound() =>
        new(UpdateBlockchainTransactionConfirmationsStoreResultKind.TransactionNotFound, Payment: null);
}

public enum UpdateBlockchainTransactionConfirmationsStoreResultKind
{
    Updated,
    Completed,
    ReorgAlerted,
    PaymentNotFound,
    PaymentNotReady,
    TransactionNotFound,
}

public sealed record BlockchainReorgMonitoringTarget(
    Guid PaymentId,
    string SupportedCurrency,
    string PaymentAddress,
    string ExpectedCryptoAmount,
    string TransactionHash,
    int CurrentConfirmations,
    Guid ProjectId = default);

public sealed record ReorgMonitoringPolicyDraft(
    int BtcRequiredConfirmations,
    int BtcMonitoringDepth,
    int LtcRequiredConfirmations,
    int LtcMonitoringDepth,
    int EthRequiredConfirmations,
    int EthMonitoringDepth);

public sealed record PaymentDraft(
    Guid Id,
    Guid IntegrationApiCredentialId,
    string ExternalReference,
    string FiatCurrency,
    long FiatAmountMinor,
    string Status,
    string PayerPageId,
    DateTimeOffset ExpiresAt,
    DateTimeOffset LateAcceptanceEndsAt,
    string? ContextUsername,
    string? ContextCustomerNumber,
    string? ContextCartName,
    string? ContextNote,
    string? ReturnUrl,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record PaymentSelectionDraft(
    Guid PaymentId,
    string SupportedCurrency,
    string ExpectedCryptoAmount,
    string PaymentAddress,
    string Network,
    long? ChainId,
    string RateSource,
    string RateValue,
    DateTimeOffset RateObservedAt,
    int ConfirmationRequirement,
    decimal PaymentTolerancePercent,
    int ReorgMonitoringDepth,
    DateTimeOffset SelectedAt);

public sealed record BlockchainObservationDraft(
    Guid Id,
    Guid PaymentId,
    string SupportedCurrency,
    string PaymentAddress,
    string TransactionHash,
    string ObservedAmount,
    DateTimeOffset ObservedAt,
    DateTimeOffset FirstObservedAt,
    int Confirmations,
    string ProviderName,
    string? ProviderObservationId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    Guid ProjectId = default);

public sealed record PaymentCompletionPolicyDraft(
    int RequiredConfirmations,
    decimal PaymentTolerancePercent);

public sealed record BlockchainTransactionConfirmationUpdateDraft(
    Guid PaymentId,
    string SupportedCurrency,
    string TransactionHash,
    int Confirmations,
    string? BlockHash,
    long? BlockHeight,
    DateTimeOffset CheckedAt,
    Guid ProjectId = default);

public sealed record PaymentOptionDraft(
    Guid PaymentId,
    string SupportedCurrency,
    string Status,
    string? UnavailableReason,
    DateTimeOffset CreatedAt);

public sealed record PaymentEventDraft(
    Guid Id,
    Guid PaymentId,
    string EventType,
    DateTimeOffset OccurredAt,
    string? Details);

public sealed record WebhookOutboxEventDraft(
    Guid Id,
    Guid PaymentId,
    Guid IntegrationApiCredentialId,
    string EventType,
    string EventVersion,
    int PayloadVersion,
    string ResourceType,
    string ResourceId,
    string Status,
    DateTimeOffset OccurredAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset NextAttemptAt,
    int AttemptCount,
    string CorrelationId);
