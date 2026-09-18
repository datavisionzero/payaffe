using System.Text.RegularExpressions;
using Payaffe.Application.Payments;

namespace Payaffe.Application.Admin;

public sealed partial class AdminNativeEthAddressPoolService(
    IAdminNativeEthAddressPoolStore store,
    IClock clock)
{
    private const int MaxImportAddresses = 10_000;

    public Task<AdminNativeEthAddressPoolSummary> GetSummaryAsync(
        Guid projectId,
        int lowCapacityThreshold,
        CancellationToken cancellationToken) =>
        store.GetSummaryAsync(projectId, lowCapacityThreshold, cancellationToken);

    public async Task<AdminNativeEthAddressPoolImportResult> ImportAsync(
        Guid projectId,
        IReadOnlyList<string>? addresses,
        int lowCapacityThreshold,
        AdminOperationContext context,
        CancellationToken cancellationToken)
    {
        if (projectId == Guid.Empty || addresses is null or { Count: 0 } ||
            addresses.Count > MaxImportAddresses ||
            lowCapacityThreshold < 0)
        {
            return AdminNativeEthAddressPoolImportResult.InvalidInput();
        }

        var normalized = addresses
            .Select(address => address?.Trim().ToLowerInvariant())
            .ToArray();
        if (normalized.Any(address =>
                string.IsNullOrWhiteSpace(address) ||
                !NativeEthAddressRegex().IsMatch(address)) ||
            normalized.Distinct(StringComparer.Ordinal).Count() != normalized.Length)
        {
            return AdminNativeEthAddressPoolImportResult.InvalidInput();
        }

        var importedAt = clock.UtcNow;
        var importId = Guid.NewGuid();
        return await store.ImportAsync(
            projectId,
            new AdminNativeEthAddressPoolImportDraft(
                projectId,
                importId,
                context.AdminAccountId,
                normalized!,
                importedAt),
            lowCapacityThreshold,
            new AdminAuditEntry(
                Guid.NewGuid(),
                importedAt,
                "admin.native_eth_address_pool.import",
                "success",
                "product_user",
                context.AdminAccountId.ToString("D"),
                context.SourceService,
                context.SourceIp,
                context.UserAgent,
                context.CorrelationId,
                "native_eth_address_pool.imported",
                "address_pool_import",
                importId.ToString("D")),
            cancellationToken);
    }

    [GeneratedRegex("^0x[0-9a-f]{40}$", RegexOptions.CultureInvariant)]
    private static partial Regex NativeEthAddressRegex();
}
