using System.ComponentModel;
using Payaffe.Application.Admin;
using Payaffe.Application.Payments;
using Payaffe.Infrastructure.Payments;
using Payaffe.Infrastructure.Webhooks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Server;

namespace Payaffe.Mcp;

/// <summary>
/// The local Admin MCP surface defined by ADR-0022. It is intentionally
/// narrower than the Admin UI: operational reads plus manual Settlement,
/// failed Webhook Delivery resend, and native ETH Address Pool import.
///
/// Audit responsibility is split deliberately. Manual Settlement and Address
/// Pool import write their Audit Log entry inside the same transaction as the
/// change, so these tools audit only the outcomes that never reach that
/// transaction. Webhook Delivery resend has no transactional audit, so the
/// tool records both its success and its failure, exactly as the Admin API
/// endpoint does for the same operation.
///
/// Every tool first checks that the configured Admin Account still exists and
/// is active. The host acts as that account, so a disabled or deleted Admin
/// must not keep operating through it; the refusal is audited.
/// </summary>
[McpServerToolType]
public sealed class AdminMcpTools
{
    [McpServerTool(
        Name = "project.list", Title = "List Projects", ReadOnly = true,
        Destructive = false, Idempotent = true, OpenWorld = false,
        UseStructuredContent = true, OutputSchemaType = typeof(ProjectListToolResult))]
    [Description("Lists Projects available to the installation-wide Admin.")]
    public static async Task<ProjectListToolResult> ListProjectsAsync(
        IServiceProvider services,
        CancellationToken cancellationToken = default)
    {
        var correlationId = NewCorrelationId();
        var refusal = await GuardAccountAsync(services, correlationId, "mcp.project.list", "project", "all", projectId: null, cancellationToken);
        if (refusal is not null)
        {
            return ProjectListToolResult.Rejected(refusal, correlationId);
        }

        await using var scope = services.CreateAsyncScope();
        var projects = await scope.ServiceProvider.GetRequiredService<AdminProjectService>()
            .ListAsync(cancellationToken);
        return ProjectListToolResult.Resolved(projects, correlationId);
    }

    [McpServerTool(
        Name = "payment.search", Title = "Search Payments", ReadOnly = true,
        Destructive = false, Idempotent = true, OpenWorld = false,
        UseStructuredContent = true, OutputSchemaType = typeof(PaymentSearchToolResult))]
    [Description("Lists recent Payments for operational review.")]
    public static async Task<PaymentSearchToolResult> SearchPaymentsAsync(
        IServiceProvider services,
        Guid projectId,
        int limit = 25,
        CancellationToken cancellationToken = default)
    {
        if (limit is < 1 or > 100)
        {
            return PaymentSearchToolResult.Rejected("validation.failed", ["limit"]);
        }

        var correlationId = NewCorrelationId();
        var refusal = await GuardAccountAsync(services, correlationId, "mcp.payment.search", "payment", "recent", projectId, cancellationToken);
        if (refusal is not null)
        {
            return PaymentSearchToolResult.Rejected(refusal, [], correlationId);
        }

        await using var scope = services.CreateAsyncScope();
        var payments = await scope.ServiceProvider.GetRequiredService<AdminPaymentQueryService>()
            .ListRecentPaymentsAsync(projectId, limit, cancellationToken);
        return PaymentSearchToolResult.Resolved(payments, correlationId);
    }

    [McpServerTool(
        Name = "payment.inspect", Title = "Inspect Payment", ReadOnly = true,
        Destructive = false, Idempotent = true, OpenWorld = false,
        UseStructuredContent = true, OutputSchemaType = typeof(PaymentInspectToolResult))]
    [Description("Inspects one Payment including operational completion and Settlement evidence.")]
    public static async Task<PaymentInspectToolResult> InspectPaymentAsync(
        IServiceProvider services,
        Guid projectId,
        Guid paymentId,
        CancellationToken cancellationToken = default)
    {
        var correlationId = NewCorrelationId();
        var refusal = await GuardAccountAsync(services, correlationId, "mcp.payment.inspect", "payment", paymentId.ToString("D"), projectId, cancellationToken);
        if (refusal is not null)
        {
            return PaymentInspectToolResult.Rejected(refusal, correlationId);
        }

        await using var scope = services.CreateAsyncScope();
        var payment = await scope.ServiceProvider.GetRequiredService<AdminPaymentQueryService>()
            .FindPaymentAsync(projectId, paymentId, cancellationToken);
        return payment is null
            ? PaymentInspectToolResult.Rejected("payment.not_found", correlationId)
            : PaymentInspectToolResult.Resolved(payment, correlationId);
    }

    [McpServerTool(
        Name = "configuration.summarize", Title = "Summarize Configuration", ReadOnly = true,
        Destructive = false, Idempotent = true, OpenWorld = false,
        UseStructuredContent = true, OutputSchemaType = typeof(ConfigurationSummaryToolResult))]
    [Description("Returns a safe operational configuration summary without secret values or references.")]
    public static async Task<ConfigurationSummaryToolResult> SummarizeConfigurationAsync(
        IServiceProvider services,
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        var correlationId = NewCorrelationId();
        var refusal = await GuardAccountAsync(services, correlationId, "mcp.configuration.summarize", "configuration", projectId.ToString("D"), projectId, cancellationToken);
        if (refusal is not null)
        {
            return ConfigurationSummaryToolResult.Rejected(projectId, correlationId, refusal);
        }

        var observation = services.GetRequiredService<IOptions<BlockchainObservationOptions>>().Value;
        await using var scope = services.CreateAsyncScope();
        var payments = await scope.ServiceProvider.GetRequiredService<IProjectPaymentConfigurationStore>()
            .FindByProjectAsync(projectId, cancellationToken);
        return payments is null
            ? ConfigurationSummaryToolResult.Rejected(projectId, correlationId)
            : ConfigurationSummaryToolResult.Resolved(
                projectId,
                payments,
                observation.Mode,
                correlationId);
    }

    [McpServerTool(
        Name = "webhook_delivery.search", Title = "Search Webhook Deliveries", ReadOnly = true,
        Destructive = false, Idempotent = true, OpenWorld = false,
        UseStructuredContent = true, OutputSchemaType = typeof(WebhookSearchToolResult))]
    [Description("Lists failed or retry-pending Webhook Deliveries.")]
    public static async Task<WebhookSearchToolResult> SearchWebhookDeliveriesAsync(
        IServiceProvider services,
        Guid projectId,
        int limit = 25,
        CancellationToken cancellationToken = default)
    {
        if (limit is < 1 or > 100)
        {
            return WebhookSearchToolResult.Rejected("validation.failed", ["limit"]);
        }

        var correlationId = NewCorrelationId();
        var refusal = await GuardAccountAsync(services, correlationId, "mcp.webhook_delivery.search", "webhook_delivery", "recent", projectId, cancellationToken);
        if (refusal is not null)
        {
            return WebhookSearchToolResult.Rejected(refusal, [], correlationId);
        }

        await using var scope = services.CreateAsyncScope();
        var deliveries = await scope.ServiceProvider.GetRequiredService<AdminWebhookDeliveryQueryService>()
            .ListResendableDeliveriesAsync(projectId, limit, cancellationToken);
        return WebhookSearchToolResult.Resolved(deliveries, correlationId);
    }

    [McpServerTool(
        Name = "audit_log.search", Title = "Search Audit Log", ReadOnly = true,
        Destructive = false, Idempotent = true, OpenWorld = false,
        UseStructuredContent = true, OutputSchemaType = typeof(AuditLogSearchToolResult))]
    [Description("Lists recent security Audit Log entries and audits the MCP access.")]
    public static async Task<AuditLogSearchToolResult> SearchAuditLogAsync(
        IServiceProvider services,
        Guid? projectId = null,
        int limit = 25,
        CancellationToken cancellationToken = default)
    {
        if (limit is < 1 or > 100)
        {
            return AuditLogSearchToolResult.Rejected("validation.failed", ["limit"]);
        }

        var correlationId = NewCorrelationId();
        var refusal = await GuardAccountAsync(services, correlationId, "mcp.audit_log.search", "audit_log", "recent", projectId, cancellationToken);
        if (refusal is not null)
        {
            return AuditLogSearchToolResult.Rejected(refusal, [], correlationId);
        }

        await using var scope = services.CreateAsyncScope();
        var entries = await scope.ServiceProvider.GetRequiredService<AdminAuditLogQueryService>()
            .ListRecentEntriesAndRecordAccessAsync(
                projectId,
                limit,
                Audit(services, correlationId, "mcp.audit_log.search", "success", "audit_log.listed", "audit_log", "recent", projectId),
                cancellationToken);
        return AuditLogSearchToolResult.Resolved(entries, correlationId);
    }

    [McpServerTool(
        Name = "address_pool.summarize", Title = "Summarize Address Pool", ReadOnly = true,
        Destructive = false, Idempotent = true, OpenWorld = false,
        UseStructuredContent = true, OutputSchemaType = typeof(AddressPoolSummaryToolResult))]
    [Description("Returns native ETH Address Pool capacity without exposing any spending material.")]
    public static async Task<AddressPoolSummaryToolResult> SummarizeAddressPoolAsync(
        IServiceProvider services,
        Guid projectId,
        CancellationToken cancellationToken = default)
    {
        var correlationId = NewCorrelationId();
        var refusal = await GuardAccountAsync(services, correlationId, "mcp.address_pool.summarize", "address_pool", "ETH", projectId, cancellationToken);
        if (refusal is not null)
        {
            return AddressPoolSummaryToolResult.Rejected(refusal, correlationId);
        }

        await using var scope = services.CreateAsyncScope();
        var summary = await scope.ServiceProvider.GetRequiredService<AdminNativeEthAddressPoolService>()
            .GetSummaryAsync(projectId, LowCapacityThreshold(services), cancellationToken);
        return AddressPoolSummaryToolResult.Resolved(summary, correlationId);
    }

    [McpServerTool(
        Name = "payment.settle", Title = "Settle Payment", ReadOnly = false,
        Destructive = false, Idempotent = true, OpenWorld = false,
        UseStructuredContent = true, OutputSchemaType = typeof(PaymentSettlementToolResult))]
    [Description("Risky action: manually settles an eligible observed or expired Payment. Client confirmation is required.")]
    public static async Task<PaymentSettlementToolResult> SettlePaymentAsync(
        IServiceProvider services,
        Guid projectId,
        Guid paymentId,
        long expectedVersion,
        string reason,
        bool confirmed,
        CancellationToken cancellationToken = default)
    {
        const string eventType = "mcp.payment.settle";
        var subjectId = paymentId.ToString("D");
        var correlationId = NewCorrelationId();

        var guard = await GuardRiskyToolAsync(services, confirmed, correlationId, eventType, "payment", subjectId, projectId, cancellationToken);
        if (guard is not null)
        {
            return PaymentSettlementToolResult.Rejected(guard, correlationId);
        }

        await using var scope = services.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<AdminPaymentQueryService>()
            .SettleAsync(projectId, paymentId, expectedVersion, reason, OperationContext(services, correlationId), cancellationToken);

        if (result.Kind == AdminPaymentSettlementResultKind.Settled)
        {
            // The settlement transaction already recorded its Audit Log entry.
            return PaymentSettlementToolResult.Resolved(result.Payment!, correlationId);
        }

        var code = result.Kind switch
        {
            AdminPaymentSettlementResultKind.NotFound => "payment.not_found",
            AdminPaymentSettlementResultKind.NotSettleable => "payment.not_settleable",
            AdminPaymentSettlementResultKind.ConcurrencyConflict => "payment.version_conflict",
            AdminPaymentSettlementResultKind.InvalidInput => "validation.failed",
            _ => "unexpected_error",
        };
        await RecordAsync(services, Audit(services, correlationId, eventType, "denied", code, "payment", subjectId, projectId), cancellationToken);
        return PaymentSettlementToolResult.Rejected(code, correlationId, result.Payment?.Status);
    }

    [McpServerTool(
        Name = "webhook_delivery.resend", Title = "Resend Webhook Delivery", ReadOnly = false,
        Destructive = false, Idempotent = false, OpenWorld = true,
        UseStructuredContent = true, OutputSchemaType = typeof(WebhookResendToolResult))]
    [Description("Risky action: resends a failed Webhook Delivery to its external endpoint. Client confirmation is required.")]
    public static async Task<WebhookResendToolResult> ResendWebhookDeliveryAsync(
        IServiceProvider services,
        Guid projectId,
        Guid webhookEventId,
        bool confirmed,
        CancellationToken cancellationToken = default)
    {
        const string eventType = "mcp.webhook_delivery.resend";
        var subjectId = webhookEventId.ToString("D");
        var correlationId = NewCorrelationId();

        var guard = await GuardRiskyToolAsync(services, confirmed, correlationId, eventType, "webhook_delivery", subjectId, projectId, cancellationToken);
        if (guard is not null)
        {
            return WebhookResendToolResult.Rejected(guard, correlationId);
        }

        await using var scope = services.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<WebhookDeliveryProcessor>()
            .ResendAsync(projectId, webhookEventId, cancellationToken);
        var resent = result.Kind == WebhookManualResendResultKind.Resent;
        var code = result.Kind switch
        {
            WebhookManualResendResultKind.Resent => "webhook_delivery.resent",
            WebhookManualResendResultKind.NotFound => "webhook_delivery.not_found",
            WebhookManualResendResultKind.NotResendable => "webhook_delivery.not_resendable",
            WebhookManualResendResultKind.InProgress => "webhook_delivery.in_progress",
            _ => "unexpected_error",
        };

        // Webhook Delivery resend has no transactional audit, so record both outcomes here.
        await RecordAsync(
            services,
            Audit(services, correlationId, eventType, resent ? "success" : "denied", code, "webhook_delivery", subjectId, projectId),
            cancellationToken);
        return resent
            ? WebhookResendToolResult.Resolved(result.Status!, correlationId)
            : WebhookResendToolResult.Rejected(code, correlationId);
    }

    [McpServerTool(
        Name = "address_pool.import_native_eth", Title = "Import Native ETH Addresses", ReadOnly = false,
        Destructive = false, Idempotent = true, OpenWorld = false,
        UseStructuredContent = true, OutputSchemaType = typeof(AddressPoolImportToolResult))]
    [Description("Risky action: imports public native ETH receiving addresses into the non-reusable Address Pool. Client confirmation is required.")]
    public static async Task<AddressPoolImportToolResult> ImportNativeEthAddressesAsync(
        IServiceProvider services,
        Guid projectId,
        string[] addresses,
        bool confirmed,
        CancellationToken cancellationToken = default)
    {
        const string eventType = "mcp.address_pool.import_native_eth";
        var correlationId = NewCorrelationId();

        var guard = await GuardRiskyToolAsync(services, confirmed, correlationId, eventType, "address_pool", "ETH", projectId, cancellationToken);
        if (guard is not null)
        {
            return AddressPoolImportToolResult.Rejected(guard, correlationId);
        }

        await using var scope = services.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<AdminNativeEthAddressPoolService>()
            .ImportAsync(projectId, addresses, LowCapacityThreshold(services), OperationContext(services, correlationId), cancellationToken);

        if (result.Kind == AdminNativeEthAddressPoolImportResultKind.Success)
        {
            // The import transaction already recorded its Audit Log entry.
            return AddressPoolImportToolResult.Resolved(result, correlationId);
        }

        var code = result.Kind switch
        {
            AdminNativeEthAddressPoolImportResultKind.InvalidInput => "validation.failed",
            AdminNativeEthAddressPoolImportResultKind.DuplicateAddress => "address_pool.duplicate_address",
            AdminNativeEthAddressPoolImportResultKind.ProjectUnavailable => "project.not_active",
            _ => "unexpected_error",
        };
        await RecordAsync(services, Audit(services, correlationId, eventType, "denied", code, "address_pool", "ETH", projectId), cancellationToken);
        return AddressPoolImportToolResult.Rejected(code, correlationId);
    }

    /// <summary>
    /// Refuses the call when the configured Admin Account is missing or not
    /// active, and audits the refusal. Returns the rejection code, or
    /// <c>null</c> when the call may proceed.
    /// </summary>
    private static async Task<string?> GuardAccountAsync(
        IServiceProvider services,
        string correlationId,
        string eventType,
        string subjectType,
        string subjectId,
        Guid? projectId,
        CancellationToken cancellationToken)
    {
        var accountId = services.GetRequiredService<IOptions<AdminMcpOptions>>().Value.AdminAccountId;
        string? status;
        await using (var scope = services.CreateAsyncScope())
        {
            status = await scope.ServiceProvider.GetRequiredService<IAdminSecurityStore>()
                .FindAccountStatusAsync(accountId, cancellationToken);
        }

        if (status == "active")
        {
            return null;
        }

        await RecordAsync(
            services,
            Audit(
                services,
                correlationId,
                eventType,
                "denied",
                status is null ? "admin_account.not_found" : "admin_account.disabled",
                subjectType,
                subjectId,
                projectId),
            cancellationToken);
        return "authentication.required";
    }

    /// <summary>
    /// Applies the checks every risky tool shares: the acting account, then
    /// confirmation, then the rate limit. Returns the rejection code when the
    /// call must not proceed, or <c>null</c> when it may.
    /// </summary>
    private static async Task<string?> GuardRiskyToolAsync(
        IServiceProvider services,
        bool confirmed,
        string correlationId,
        string eventType,
        string subjectType,
        string subjectId,
        Guid projectId,
        CancellationToken cancellationToken)
    {
        var refusal = await GuardAccountAsync(services, correlationId, eventType, subjectType, subjectId, projectId, cancellationToken);
        if (refusal is not null)
        {
            return refusal;
        }

        if (!confirmed)
        {
            await RecordAsync(
                services,
                Audit(services, correlationId, eventType, "denied", "confirmation.required", subjectType, subjectId, projectId),
                cancellationToken);
            return "confirmation.required";
        }

        if (!services.GetRequiredService<AdminMcpRateLimiter>().TryAcquire(DateTimeOffset.UtcNow))
        {
            await RecordAsync(
                services,
                Audit(services, correlationId, eventType, "denied", "rate_limited", subjectType, subjectId, projectId),
                cancellationToken);
            return "rate_limited";
        }

        return null;
    }

    private static string NewCorrelationId() => Guid.NewGuid().ToString("N");

    private static int LowCapacityThreshold(IServiceProvider services) =>
        services.GetRequiredService<IOptions<PaymentAddressOptions>>().Value.NativeEthLowCapacityThreshold;

    private static AdminOperationContext OperationContext(IServiceProvider services, string correlationId) =>
        new(
            services.GetRequiredService<IOptions<AdminMcpOptions>>().Value.AdminAccountId,
            SourceIp: null,
            UserAgent: null,
            correlationId,
            SourceService: "mcp");

    private static AdminAuditEntry Audit(
        IServiceProvider services,
        string correlationId,
        string eventType,
        string outcome,
        string reasonCode,
        string subjectType,
        string subjectId,
        Guid? projectId = null) =>
        new(
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            eventType,
            outcome,
            "product_user",
            services.GetRequiredService<IOptions<AdminMcpOptions>>().Value.AdminAccountId.ToString("D"),
            "mcp",
            SourceIp: null,
            UserAgent: null,
            correlationId,
            reasonCode,
            subjectType,
            subjectId,
            projectId);

    private static async Task RecordAsync(
        IServiceProvider services,
        AdminAuditEntry entry,
        CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IAdminSecurityStore>()
            .RecordSecurityAuditAsync(entry, cancellationToken);
    }
}
