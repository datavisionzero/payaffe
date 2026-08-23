namespace Payaffe.Application.Admin;

public sealed record AdminLoginStartResult(AdminLoginStartResultKind Kind, Guid? ChallengeId)
{
    public static AdminLoginStartResult MfaRequired(Guid challengeId) =>
        new(AdminLoginStartResultKind.MfaRequired, challengeId);

    public static AdminLoginStartResult InvalidCredentials() =>
        new(AdminLoginStartResultKind.InvalidCredentials, ChallengeId: null);
}

public enum AdminLoginStartResultKind
{
    MfaRequired,
    InvalidCredentials,
}
