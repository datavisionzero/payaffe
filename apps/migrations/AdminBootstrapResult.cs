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

    public static AdminBootstrapResult AlreadyBootstrapped() =>
        new(AdminBootstrapStatus.AlreadyBootstrapped, null, []);
}

/// <summary>
/// The outcomes creating the first Admin Account can have.
/// </summary>
/// <remarks>
/// `TotpUnavailable` and `TotpInvalid` are gone with ADR 0028. They were the
/// two ways this command could fail after prompting for a password and before
/// producing anything, which is what made it the step people gave up on.
/// </remarks>
public enum AdminBootstrapStatus
{
    Created,
    InvalidInput,
    AlreadyBootstrapped,
}
