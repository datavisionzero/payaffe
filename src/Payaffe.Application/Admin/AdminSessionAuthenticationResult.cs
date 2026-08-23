namespace Payaffe.Application.Admin;

public sealed record AdminSessionAuthenticationResult(
    AdminSessionAuthenticationResultKind Kind,
    AdminSessionPrincipal? Principal)
{
    public static AdminSessionAuthenticationResult Authenticated(AdminSessionPrincipal principal) =>
        new(AdminSessionAuthenticationResultKind.Authenticated, principal);

    public static AdminSessionAuthenticationResult Invalid() =>
        new(AdminSessionAuthenticationResultKind.Invalid, Principal: null);
}

public sealed record AdminSessionPrincipal(
    Guid AdminAccountId,
    string Username,
    Guid SessionId,
    DateTimeOffset MfaAuthenticatedAt,
    DateTimeOffset StepUpAuthenticatedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset IdleExpiresAt);

public enum AdminSessionAuthenticationResultKind
{
    Authenticated,
    Invalid,
}
