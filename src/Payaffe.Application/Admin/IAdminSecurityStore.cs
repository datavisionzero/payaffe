namespace Payaffe.Application.Admin;

public interface IAdminSecurityStore
{
    Task<AdminAccountReadModel?> FindByNormalizedUsernameAsync(
        string normalizedUsername,
        CancellationToken cancellationToken);

    Task RecordSuccessfulPasswordVerificationAsync(
        Guid adminAccountId,
        DateTimeOffset occurredAt,
        AdminLoginChallengeDraft loginChallenge,
        AdminAuditEntry auditEntry,
        CancellationToken cancellationToken);

    /// <summary>
    /// A sign-in that is finished once the password is verified, because the
    /// account has no second factor enrolled (ADR 0028).
    /// </summary>
    /// <remarks>
    /// Deliberately its own method rather than a nullable challenge on
    /// <see cref="RecordSuccessfulPasswordVerificationAsync"/>: the two write
    /// different rows and mean different things, and a store method that
    /// sometimes issues a session and sometimes does not is one a reader has to
    /// trace to understand.
    /// </remarks>
    Task RecordPasswordOnlyAuthenticationAsync(
        Guid adminAccountId,
        DateTimeOffset occurredAt,
        AdminSessionDraft session,
        AdminAuditEntry auditEntry,
        CancellationToken cancellationToken);

    Task RecordFailedPasswordVerificationAsync(
        Guid? adminAccountId,
        DateTimeOffset occurredAt,
        int? failedPasswordAttemptCount,
        DateTimeOffset? lockedUntil,
        AdminAuditEntry auditEntry,
        CancellationToken cancellationToken);

    Task<AdminLoginChallengeReadModel?> FindLoginChallengeAsync(
        Guid challengeId,
        CancellationToken cancellationToken);

    Task RecordFailedMfaVerificationAsync(
        Guid? challengeId,
        DateTimeOffset occurredAt,
        int? failedAttemptCount,
        DateTimeOffset? consumedAt,
        AdminAuditEntry auditEntry,
        CancellationToken cancellationToken);

    Task CompleteMfaVerificationAsync(
        Guid challengeId,
        DateTimeOffset consumedAt,
        AdminSessionDraft session,
        AdminAuditEntry auditEntry,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<AdminRecoveryCodeReadModel>> FindActiveRecoveryCodesAsync(
        Guid adminAccountId,
        CancellationToken cancellationToken);

    Task CompleteMfaVerificationWithRecoveryCodeAsync(
        Guid challengeId,
        Guid recoveryCodeId,
        DateTimeOffset consumedAt,
        AdminSessionDraft session,
        AdminAuditEntry mfaAuditEntry,
        AdminAuditEntry recoveryCodeAuditEntry,
        CancellationToken cancellationToken);

    Task<AdminSessionReadModel?> FindSessionByTokenHashAsync(
        string tokenHash,
        CancellationToken cancellationToken);

    Task RefreshSessionAsync(
        Guid sessionId,
        DateTimeOffset lastSeenAt,
        DateTimeOffset idleExpiresAt,
        CancellationToken cancellationToken);

    /// <param name="secondFactorVerified">
    /// True when the step-up verified a code; the session then counts as having
    /// cleared a second factor even if it began on the password alone.
    /// </param>
    Task RecordSuccessfulStepUpAsync(
        Guid sessionId,
        DateTimeOffset occurredAt,
        DateTimeOffset idleExpiresAt,
        bool secondFactorVerified,
        AdminAuditEntry auditEntry,
        CancellationToken cancellationToken);

    /// <summary>
    /// Revokes the account's active sessions that never cleared a second
    /// factor, except <paramref name="currentSessionId"/>, and records
    /// <paramref name="auditEntry"/> when any was revoked.
    /// </summary>
    Task<int> RevokeSessionsWithoutSecondFactorAsync(
        Guid adminAccountId,
        Guid currentSessionId,
        DateTimeOffset revokedAt,
        AdminAuditEntry auditEntry,
        CancellationToken cancellationToken);

    Task RecordFailedStepUpAsync(
        AdminAuditEntry auditEntry,
        CancellationToken cancellationToken);

    Task ReplaceRecoveryCodesAsync(
        Guid adminAccountId,
        DateTimeOffset occurredAt,
        IReadOnlyList<AdminRecoveryCodeDraft> recoveryCodes,
        AdminAuditEntry generatedAuditEntry,
        AdminAuditEntry revokedAuditEntry,
        CancellationToken cancellationToken);

    Task RecordSecurityAuditAsync(
        AdminAuditEntry auditEntry,
        CancellationToken cancellationToken);

    Task RevokeSessionAsync(
        Guid sessionId,
        DateTimeOffset revokedAt,
        AdminAuditEntry auditEntry,
        CancellationToken cancellationToken);
}
