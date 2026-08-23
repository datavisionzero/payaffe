namespace Payaffe.Application.Admin;

/// <summary>
/// What a password sign-in produced.
/// </summary>
/// <remarks>
/// Two ways forward rather than one, because TOTP is optional (ADR 0028).
/// An account that has enrolled a second factor gets a challenge; one that has
/// not is signed in, and the session it gets is the same session the second
/// step would have produced.
/// </remarks>
public sealed record AdminLoginStartResult(
    AdminLoginStartResultKind Kind,
    Guid? ChallengeId,
    string? SessionToken,
    DateTimeOffset? SessionExpiresAt)
{
    public static AdminLoginStartResult MfaRequired(Guid challengeId) =>
        new(AdminLoginStartResultKind.MfaRequired, challengeId, SessionToken: null, SessionExpiresAt: null);

    public static AdminLoginStartResult Authenticated(string sessionToken, DateTimeOffset expiresAt) =>
        new(AdminLoginStartResultKind.Authenticated, ChallengeId: null, sessionToken, expiresAt);

    public static AdminLoginStartResult InvalidCredentials() =>
        new(AdminLoginStartResultKind.InvalidCredentials, ChallengeId: null, SessionToken: null, SessionExpiresAt: null);
}

public enum AdminLoginStartResultKind
{
    MfaRequired,

    /// <summary>Signed in on the password alone; no second factor is enrolled.</summary>
    Authenticated,
    InvalidCredentials,
}
