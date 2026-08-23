using Payaffe.Application.Admin;

namespace Payaffe.Mcp;

public sealed record PaymentSearchToolResult(
    string Status,
    string Code,
    IReadOnlyList<AdminPaymentSummaryReadModel> Payments,
    IReadOnlyList<string> Fields,
    string CorrelationId,
    string Summary)
{
    public static PaymentSearchToolResult Resolved(IReadOnlyList<AdminPaymentSummaryReadModel> payments, string correlationId) =>
        new("resolved", "payments.listed", payments, [], correlationId, $"Found {payments.Count} Payments.");

    public static PaymentSearchToolResult Rejected(string code, IReadOnlyList<string> fields) =>
        new("rejected", code, [], fields, Guid.NewGuid().ToString("N"), "Payment search was rejected.");
}

public sealed record PaymentInspectToolResult(
    string Status,
    string Code,
    AdminPaymentDetailReadModel? Payment,
    string CorrelationId,
    string Summary)
{
    public static PaymentInspectToolResult Resolved(AdminPaymentDetailReadModel payment, string correlationId) =>
        new("resolved", "payment.inspected", payment, correlationId, "Payment inspected.");

    public static PaymentInspectToolResult Rejected(string code, string correlationId) =>
        new("rejected", code, null, correlationId, "Payment inspection was rejected.");
}

public sealed record ConfigurationSummaryToolResult(
    string Status,
    string Code,
    string ObservationMode,
    TimeSpan PaymentExpiration,
    TimeSpan LateAcceptanceWindow,
    decimal PaymentTolerancePercent,
    string CorrelationId,
    string Summary);

public sealed record WebhookSearchToolResult(
    string Status,
    string Code,
    IReadOnlyList<AdminWebhookDeliveryReadModel> Deliveries,
    IReadOnlyList<string> Fields,
    string CorrelationId,
    string Summary)
{
    public static WebhookSearchToolResult Resolved(IReadOnlyList<AdminWebhookDeliveryReadModel> deliveries, string correlationId) =>
        new("resolved", "webhook_deliveries.listed", deliveries, [], correlationId,
            $"Found {deliveries.Count} resendable Webhook Deliveries.");

    public static WebhookSearchToolResult Rejected(string code, IReadOnlyList<string> fields) =>
        new("rejected", code, [], fields, Guid.NewGuid().ToString("N"), "Webhook Delivery search was rejected.");
}

public sealed record AuditLogSearchToolResult(
    string Status,
    string Code,
    IReadOnlyList<AdminAuditLogEntryReadModel> Entries,
    IReadOnlyList<string> Fields,
    string CorrelationId,
    string Summary)
{
    public static AuditLogSearchToolResult Resolved(IReadOnlyList<AdminAuditLogEntryReadModel> entries, string correlationId) =>
        new("resolved", "audit_log.listed", entries, [], correlationId, $"Found {entries.Count} Audit Log entries.");

    public static AuditLogSearchToolResult Rejected(string code, IReadOnlyList<string> fields) =>
        new("rejected", code, [], fields, Guid.NewGuid().ToString("N"), "Audit Log search was rejected.");
}

public sealed record AddressPoolSummaryToolResult(
    string Status,
    string Code,
    AdminNativeEthAddressPoolSummary Pool,
    string CorrelationId,
    string Summary);

public sealed record PaymentSettlementToolResult(
    string Status,
    string Code,
    Guid? PaymentId,
    string? PaymentStatus,
    DateTimeOffset? SettledAt,
    string? CurrentStatus,
    string CorrelationId,
    string Summary)
{
    public static PaymentSettlementToolResult Resolved(AdminPaymentDetailReadModel payment, string correlationId) =>
        new("settled", "payment.settled", payment.PaymentId, payment.Status, payment.SettledAt, payment.Status,
            correlationId, "Payment settled.");

    public static PaymentSettlementToolResult Rejected(string code, string correlationId, string? currentStatus = null) =>
        new("rejected", code, null, null, null, currentStatus, correlationId, "Payment Settlement was rejected.");
}

public sealed record WebhookResendToolResult(
    string Status,
    string Code,
    string? DeliveryStatus,
    string CorrelationId,
    string Summary)
{
    public static WebhookResendToolResult Resolved(string deliveryStatus, string correlationId) =>
        new("resent", "webhook_delivery.resent", deliveryStatus, correlationId, "Webhook Delivery resent.");

    public static WebhookResendToolResult Rejected(string code, string correlationId) =>
        new("rejected", code, null, correlationId, "Webhook Delivery resend was rejected.");
}

public sealed record AddressPoolImportToolResult(
    string Status,
    string Code,
    Guid? ImportId,
    int ImportedCount,
    AdminNativeEthAddressPoolSummary? Pool,
    string CorrelationId,
    string Summary)
{
    public static AddressPoolImportToolResult Resolved(AdminNativeEthAddressPoolImportResult result, string correlationId) =>
        new("imported", "native_eth_address_pool.imported", result.ImportId, result.ImportedCount, result.Summary,
            correlationId, $"Imported {result.ImportedCount} native ETH addresses.");

    public static AddressPoolImportToolResult Rejected(string code, string correlationId) =>
        new("rejected", code, null, 0, null, correlationId, "Native ETH Address Pool import was rejected.");
}
