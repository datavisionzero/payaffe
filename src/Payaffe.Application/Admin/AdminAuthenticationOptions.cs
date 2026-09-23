namespace Payaffe.Application.Admin;

public sealed class AdminAuthenticationOptions
{
    public int MaxFailedPasswordAttempts { get; set; } = 5;

    public TimeSpan LockoutDuration { get; set; } = TimeSpan.FromMinutes(15);

    public TimeSpan LoginChallengeLifetime { get; set; } = TimeSpan.FromMinutes(5);

    public int MaxFailedMfaAttempts { get; set; } = 5;

    /// <summary>
    /// Wrong second-factor codes an account may accumulate across challenges
    /// and step-ups before its second factor is locked for
    /// <see cref="LockoutDuration"/>. A correct password does not reset it;
    /// only a verified second factor does.
    /// </summary>
    public int MaxFailedSecondFactorAttempts { get; set; } = 10;

    public int TotpAllowedTimeStepSkew { get; set; } = 1;

    public TimeSpan SessionAbsoluteLifetime { get; set; } = TimeSpan.FromDays(7);

    public TimeSpan SessionIdleLifetime { get; set; } = TimeSpan.FromHours(12);

    public TimeSpan StepUpLifetime { get; set; } = TimeSpan.FromMinutes(15);

    public int RecoveryCodeCount { get; set; } = 10;

    public int RateLimitPermitLimit { get; set; } = 30;

    public TimeSpan RateLimitWindow { get; set; } = TimeSpan.FromMinutes(5);
}
