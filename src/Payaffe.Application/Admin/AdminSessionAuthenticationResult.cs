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
    DateTimeOffset? MfaAuthenticatedAt,
    DateTimeOffset? StepUpAuthenticatedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset IdleExpiresAt,
    // The account's enrollment now, not when the session began: a factor
    // enrolled after a password-only sign-in has to govern that session too.
    bool HasEnrolledSecondFactor);

public enum AdminSessionAuthenticationResultKind
{
    Authenticated,
    Invalid,
}
