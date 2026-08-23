namespace Payaffe.Application.Admin;

public sealed record AdminStepUpResult(
    AdminStepUpResultKind Kind,
    DateTimeOffset? StepUpAuthenticatedAt,
    DateTimeOffset? IdleExpiresAt)
{
    public static AdminStepUpResult Authenticated(
        DateTimeOffset stepUpAuthenticatedAt,
        DateTimeOffset idleExpiresAt) =>
        new(AdminStepUpResultKind.Authenticated, stepUpAuthenticatedAt, idleExpiresAt);

    public static AdminStepUpResult Invalid() =>
        new(AdminStepUpResultKind.Invalid, StepUpAuthenticatedAt: null, IdleExpiresAt: null);
}

public enum AdminStepUpResultKind
{
    Authenticated,
    Invalid,
}
