using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Payaffe.Application.Payments;

namespace Payaffe.Application.Admin;

public sealed partial class AdminProjectService(
    IAdminProjectStore store,
    IClock clock,
    IOptions<PaymentApplicationOptions> paymentOptions,
    IOptions<AdminProjectDefaultsOptions> projectDefaults)
{
    public Task<IReadOnlyList<AdminProjectReadModel>> ListAsync(CancellationToken cancellationToken) =>
        store.ListAsync(cancellationToken);

    public async Task<AdminProjectResult> CreateAsync(
        string? name,
        string? slug,
        AdminOperationContext context,
        CancellationToken cancellationToken)
    {
        var normalizedName = name?.Trim();
        var normalizedSlug = slug?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalizedName) || normalizedName.Length > 100 ||
            string.IsNullOrWhiteSpace(normalizedSlug) || normalizedSlug.Length > 63 ||
            !SlugPattern().IsMatch(normalizedSlug))
        {
            return AdminProjectResult.InvalidInput();
        }

        var now = clock.UtcNow;
        var projectId = Guid.NewGuid();
        var payments = paymentOptions.Value;
        var defaults = projectDefaults.Value;
        return await store.CreateAsync(
            new AdminProjectDraft(
                projectId,
                normalizedName,
                normalizedSlug,
                now,
                checked((long)payments.PaymentExpiration.TotalSeconds),
                checked((long)payments.LateAcceptanceWindow.TotalSeconds),
                payments.PaymentTolerancePercent,
                payments.BtcConfirmationRequirement,
                payments.LtcConfirmationRequirement,
                payments.EthConfirmationRequirement,
                payments.BtcReorgMonitoringDepth,
                payments.LtcReorgMonitoringDepth,
                payments.EthReorgMonitoringDepth,
                defaults.NativeEthLowCapacityThreshold),
            Audit(context, projectId, now, "admin.project.create", "project.created"),
            cancellationToken);
    }

    public Task<AdminProjectResult> ChangeStatusAsync(
        Guid projectId,
        long expectedVersion,
        string? status,
        AdminOperationContext context,
        CancellationToken cancellationToken)
    {
        var normalizedStatus = status?.Trim().ToLowerInvariant();
        if (projectId == Guid.Empty || expectedVersion <= 0 || normalizedStatus is not ("active" or "disabled" or "archived"))
        {
            return Task.FromResult(AdminProjectResult.InvalidInput());
        }

        var now = clock.UtcNow;
        return store.ChangeStatusAsync(
            projectId,
            expectedVersion,
            normalizedStatus,
            now,
            Audit(context, projectId, now, "admin.project.change_status", $"project.{normalizedStatus}"),
            cancellationToken);
    }

    private static AdminAuditEntry Audit(
        AdminOperationContext context,
        Guid projectId,
        DateTimeOffset occurredAt,
        string eventType,
        string reasonCode) => new(
            Guid.NewGuid(), occurredAt, eventType, "success", "product_user",
            context.AdminAccountId.ToString("D"), context.SourceService, context.SourceIp,
            context.UserAgent, context.CorrelationId, reasonCode, "project", projectId.ToString("D"), projectId);

    [GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex SlugPattern();
}

public sealed class AdminProjectDefaultsOptions
{
    public int NativeEthLowCapacityThreshold { get; set; } = 20;
}
