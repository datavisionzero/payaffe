namespace Payaffe.Migrations;

public sealed record AdminBootstrapResult(
    AdminBootstrapStatus Status,
    Guid? AdminAccountId,
    IReadOnlyList<string> RecoveryCodes)
{
    public static AdminBootstrapResult Created(Guid adminAccountId, IReadOnlyList<string> recoveryCodes) =>
        new(AdminBootstrapStatus.Created, adminAccountId, recoveryCodes);

    public static AdminBootstrapResult InvalidInput() =>
        new(AdminBootstrapStatus.InvalidInput, null, []);

    public static AdminBootstrapResult TotpUnavailable() =>
        new(AdminBootstrapStatus.TotpUnavailable, null, []);

    public static AdminBootstrapResult TotpInvalid() =>
        new(AdminBootstrapStatus.TotpInvalid, null, []);

    public static AdminBootstrapResult AlreadyBootstrapped() =>
        new(AdminBootstrapStatus.AlreadyBootstrapped, null, []);
}

public enum AdminBootstrapStatus
{
    Created,
    InvalidInput,
    TotpUnavailable,
    TotpInvalid,
    AlreadyBootstrapped,
}
