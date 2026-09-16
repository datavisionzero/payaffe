namespace Payaffe.Application.Admin;

public sealed record AdminProjectReadModel(
    Guid ProjectId,
    string Name,
    string Slug,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    long Version);

public sealed record AdminProjectDraft(
    Guid ProjectId,
    string Name,
    string Slug,
    DateTimeOffset CreatedAt,
    long PaymentExpirationSeconds,
    long LateAcceptanceWindowSeconds,
    decimal PaymentTolerancePercent,
    int BtcConfirmationRequirement,
    int LtcConfirmationRequirement,
    int EthConfirmationRequirement,
    int BtcReorgMonitoringDepth,
    int LtcReorgMonitoringDepth,
    int EthReorgMonitoringDepth,
    int NativeEthLowCapacityThreshold);

public sealed record AdminProjectResult(
    AdminProjectResultKind Kind,
    AdminProjectReadModel? Project)
{
    public static AdminProjectResult Updated(AdminProjectReadModel project) => new(AdminProjectResultKind.Updated, project);
    public static AdminProjectResult InvalidInput() => new(AdminProjectResultKind.InvalidInput, null);
    public static AdminProjectResult NotFound() => new(AdminProjectResultKind.NotFound, null);
    public static AdminProjectResult SlugConflict() => new(AdminProjectResultKind.SlugConflict, null);
    public static AdminProjectResult InvalidTransition(AdminProjectReadModel project) => new(AdminProjectResultKind.InvalidTransition, project);
    public static AdminProjectResult HasActiveWork(AdminProjectReadModel project) => new(AdminProjectResultKind.HasActiveWork, project);
    public static AdminProjectResult ConcurrencyConflict(AdminProjectReadModel project) => new(AdminProjectResultKind.ConcurrencyConflict, project);
}

public enum AdminProjectResultKind
{
    Updated,
    InvalidInput,
    NotFound,
    SlugConflict,
    InvalidTransition,
    HasActiveWork,
    ConcurrencyConflict,
}
