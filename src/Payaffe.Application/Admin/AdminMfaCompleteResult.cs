namespace Payaffe.Application.Admin;

public sealed record AdminMfaCompleteResult(
    AdminMfaCompleteResultKind Kind,
    string? SessionToken,
    DateTimeOffset? ExpiresAt)
{
    public static AdminMfaCompleteResult Authenticated(string sessionToken, DateTimeOffset expiresAt) =>
        new(AdminMfaCompleteResultKind.Authenticated, sessionToken, expiresAt);

    public static AdminMfaCompleteResult Invalid() =>
        new(AdminMfaCompleteResultKind.Invalid, SessionToken: null, ExpiresAt: null);
}

public enum AdminMfaCompleteResultKind
{
    Authenticated,
    Invalid,
}
