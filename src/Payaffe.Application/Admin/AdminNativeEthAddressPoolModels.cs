namespace Payaffe.Application.Admin;

public sealed record AdminNativeEthAddressPoolSummary(
    int UnusedCount,
    int AssignedCount,
    int RetiredCount,
    int LowCapacityThreshold,
    bool IsLowCapacity);

public sealed record AdminNativeEthAddressPoolImportResult(
    AdminNativeEthAddressPoolImportResultKind Kind,
    Guid? ImportId,
    int ImportedCount,
    AdminNativeEthAddressPoolSummary? Summary)
{
    public static AdminNativeEthAddressPoolImportResult Success(
        Guid importId,
        int importedCount,
        AdminNativeEthAddressPoolSummary summary) =>
        new(AdminNativeEthAddressPoolImportResultKind.Success, importId, importedCount, summary);

    public static AdminNativeEthAddressPoolImportResult InvalidInput() =>
        new(AdminNativeEthAddressPoolImportResultKind.InvalidInput, null, 0, null);

    public static AdminNativeEthAddressPoolImportResult DuplicateAddress() =>
        new(AdminNativeEthAddressPoolImportResultKind.DuplicateAddress, null, 0, null);
}

public enum AdminNativeEthAddressPoolImportResultKind
{
    Success,
    InvalidInput,
    DuplicateAddress,
}

public sealed record AdminNativeEthAddressPoolImportDraft(
    Guid ImportId,
    Guid ImportedByAdminAccountId,
    IReadOnlyList<string> Addresses,
    DateTimeOffset ImportedAt);
