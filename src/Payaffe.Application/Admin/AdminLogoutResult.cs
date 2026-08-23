namespace Payaffe.Application.Admin;

public sealed record AdminLogoutResult(AdminLogoutResultKind Kind)
{
    public static AdminLogoutResult SessionRevoked() =>
        new(AdminLogoutResultKind.SessionRevoked);

    public static AdminLogoutResult NoActiveSession() =>
        new(AdminLogoutResultKind.NoActiveSession);
}

public enum AdminLogoutResultKind
{
    SessionRevoked,
    NoActiveSession,
}
