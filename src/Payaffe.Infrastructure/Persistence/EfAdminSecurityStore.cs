using Payaffe.Application.Admin;
using Payaffe.Infrastructure.Persistence.Records;
using Microsoft.EntityFrameworkCore;

namespace Payaffe.Infrastructure.Persistence;

/// <remarks>
/// Every write to an account, a login challenge or a Recovery Code increments
/// its <c>Version</c>, which the model marks as a concurrency token. Without
/// that, parallel read-modify-write calls all matched <c>version = 1</c> and
/// all succeeded: a Recovery Code or challenge could be redeemed twice, and
/// parallel failures overwrote each other's count. A write that loses the race
/// re-reads and re-applies, so counters count every failure and single-use
/// items are used once.
/// </remarks>
public sealed class EfAdminSecurityStore(PayaffeDbContext dbContext) : IAdminSecurityStore
{
    private const int MaxConcurrencyAttempts = 20;

    public async Task<AdminAccountReadModel?> FindByNormalizedUsernameAsync(
        string normalizedUsername,
        CancellationToken cancellationToken)
    {
        return await dbContext.AdminAccounts
            .AsNoTracking()
            .Where(account => account.NormalizedUsername == normalizedUsername)
            .Select(account => new AdminAccountReadModel(
                account.Id,
                account.Username,
                account.NormalizedUsername,
                account.PasswordHash,
                account.TotpSecretReference,
                account.Status,
                account.FailedPasswordAttemptCount,
                account.LockedUntil))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task RecordSuccessfulPasswordVerificationAsync(
        Guid adminAccountId,
        DateTimeOffset occurredAt,
        AdminLoginChallengeDraft loginChallenge,
        AdminAuditEntry auditEntry,
        CancellationToken cancellationToken)
    {
        await SaveWithRetryAsync(
            async () =>
            {
                await ResetPasswordFailuresAsync(adminAccountId, occurredAt, cancellationToken);

                dbContext.AdminLoginChallenges.Add(new AdminLoginChallengeRecord
                {
                    Id = loginChallenge.Id,
                    AdminAccountId = loginChallenge.AdminAccountId,
                    ExpiresAt = loginChallenge.ExpiresAt,
                    FailedAttemptCount = 0,
                    CreatedAt = loginChallenge.CreatedAt,
                });

                AddAuditEntry(auditEntry);
                await dbContext.SaveChangesAsync(cancellationToken);
                return true;
            },
            cancellationToken);
    }

    public async Task RecordPasswordOnlyAuthenticationAsync(
        Guid adminAccountId,
        DateTimeOffset occurredAt,
        AdminSessionDraft session,
        AdminAuditEntry auditEntry,
        CancellationToken cancellationToken)
    {
        await SaveWithRetryAsync(
            async () =>
            {
                await ResetPasswordFailuresAsync(adminAccountId, occurredAt, cancellationToken);

                // No challenge row: there is no second step to remember the state of.
                AddSession(session);
                AddAuditEntry(auditEntry);
                await dbContext.SaveChangesAsync(cancellationToken);
                return true;
            },
            cancellationToken);
    }

    public async Task<AdminFailedAttemptOutcome> RecordFailedPasswordVerificationAsync(
        Guid adminAccountId,
        DateTimeOffset occurredAt,
        int maxFailedAttempts,
        TimeSpan lockoutDuration,
        Func<AdminFailedAttemptOutcome, AdminAuditEntry> createAuditEntry,
        CancellationToken cancellationToken)
    {
        return await SaveWithRetryAsync(
            async () =>
            {
                var adminAccount = await dbContext.AdminAccounts.SingleAsync(
                    account => account.Id == adminAccountId,
                    cancellationToken);
                adminAccount.FailedPasswordAttemptCount++;
                var outcome = new AdminFailedAttemptOutcome(
                    adminAccount.FailedPasswordAttemptCount,
                    adminAccount.FailedPasswordAttemptCount >= maxFailedAttempts);
                if (outcome.LimitReached)
                {
                    adminAccount.LockedUntil = occurredAt.Add(lockoutDuration);
                }

                adminAccount.UpdatedAt = occurredAt;
                adminAccount.Version++;

                AddAuditEntry(createAuditEntry(outcome));
                await dbContext.SaveChangesAsync(cancellationToken);
                return outcome;
            },
            cancellationToken);
    }

    public async Task<AdminLoginChallengeReadModel?> FindLoginChallengeAsync(
        Guid challengeId,
        CancellationToken cancellationToken)
    {
        return await dbContext.AdminLoginChallenges
            .AsNoTracking()
            .Where(challenge => challenge.Id == challengeId)
            .Join(
                dbContext.AdminAccounts,
                challenge => challenge.AdminAccountId,
                account => account.Id,
                (challenge, account) => new AdminLoginChallengeReadModel(
                    challenge.Id,
                    challenge.AdminAccountId,
                    account.TotpSecretReference ?? string.Empty,
                    account.Status,
                    challenge.ExpiresAt,
                    challenge.FailedAttemptCount,
                    challenge.ConsumedAt,
                    account.SecondFactorLockedUntil))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<AdminFailedAttemptOutcome> RecordFailedMfaVerificationAsync(
        Guid challengeId,
        DateTimeOffset occurredAt,
        int maxFailedAttempts,
        Func<AdminFailedAttemptOutcome, AdminAuditEntry> createAuditEntry,
        CancellationToken cancellationToken)
    {
        return await SaveWithRetryAsync(
            async () =>
            {
                var challenge = await dbContext.AdminLoginChallenges.SingleAsync(
                    candidate => candidate.Id == challengeId,
                    cancellationToken);
                challenge.FailedAttemptCount++;
                var outcome = new AdminFailedAttemptOutcome(
                    challenge.FailedAttemptCount,
                    challenge.FailedAttemptCount >= maxFailedAttempts);
                if (outcome.LimitReached)
                {
                    challenge.ConsumedAt ??= occurredAt;
                }

                challenge.Version++;

                AddAuditEntry(createAuditEntry(outcome));
                await dbContext.SaveChangesAsync(cancellationToken);
                return outcome;
            },
            cancellationToken);
    }

    public async Task<bool> CompleteMfaVerificationAsync(
        Guid challengeId,
        DateTimeOffset consumedAt,
        long acceptedTimeStep,
        AdminSessionDraft session,
        AdminAuditEntry auditEntry,
        CancellationToken cancellationToken)
    {
        return await SaveWithRetryAsync(
            async () =>
            {
                if (!await ConsumeChallengeAsync(challengeId, consumedAt, cancellationToken) ||
                    !await AcceptSecondFactorAsync(session.AdminAccountId, consumedAt, acceptedTimeStep, cancellationToken))
                {
                    dbContext.ChangeTracker.Clear();
                    return false;
                }

                AddSession(session);
                AddAuditEntry(auditEntry);
                await dbContext.SaveChangesAsync(cancellationToken);
                return true;
            },
            cancellationToken);
    }

    public async Task<IReadOnlyList<AdminRecoveryCodeReadModel>> FindActiveRecoveryCodesAsync(
        Guid adminAccountId,
        CancellationToken cancellationToken)
    {
        return await dbContext.AdminRecoveryCodes
            .AsNoTracking()
            .Where(code => code.AdminAccountId == adminAccountId && code.Status == "active")
            .Select(code => new AdminRecoveryCodeReadModel(code.Id, code.CodeHash))
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> CompleteMfaVerificationWithRecoveryCodeAsync(
        Guid challengeId,
        Guid recoveryCodeId,
        DateTimeOffset consumedAt,
        AdminSessionDraft session,
        AdminAuditEntry mfaAuditEntry,
        AdminAuditEntry recoveryCodeAuditEntry,
        CancellationToken cancellationToken)
    {
        return await SaveWithRetryAsync(
            async () =>
            {
                if (!await ConsumeChallengeAsync(challengeId, consumedAt, cancellationToken))
                {
                    return false;
                }

                var recoveryCode = await dbContext.AdminRecoveryCodes.SingleAsync(
                    candidate => candidate.Id == recoveryCodeId,
                    cancellationToken);
                if (recoveryCode.Status != "active")
                {
                    dbContext.ChangeTracker.Clear();
                    return false;
                }

                recoveryCode.Status = "used";
                recoveryCode.UsedAt = consumedAt;
                recoveryCode.Version++;
                await AcceptSecondFactorAsync(session.AdminAccountId, consumedAt, acceptedTimeStep: null, cancellationToken);

                AddSession(session);
                AddAuditEntry(mfaAuditEntry);
                AddAuditEntry(recoveryCodeAuditEntry);
                await dbContext.SaveChangesAsync(cancellationToken);
                return true;
            },
            cancellationToken);
    }

    public async Task<AdminSessionReadModel?> FindSessionByTokenHashAsync(
        string tokenHash,
        CancellationToken cancellationToken)
    {
        return await dbContext.AdminSessions
            .AsNoTracking()
            .Where(session => session.TokenHash == tokenHash)
            .Join(
                dbContext.AdminAccounts,
                session => session.AdminAccountId,
                account => account.Id,
                (session, account) => new AdminSessionReadModel(
                    session.Id,
                    session.AdminAccountId,
                    account.Username,
                    account.Status,
                    account.TotpSecretReference ?? string.Empty,
                    session.ExpiresAt,
                    session.IdleExpiresAt,
                    session.MfaAuthenticatedAt,
                    session.StepUpAuthenticatedAt,
                    session.RevokedAt,
                    account.SecondFactorLockedUntil))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task RefreshSessionAsync(
        Guid sessionId,
        DateTimeOffset lastSeenAt,
        DateTimeOffset idleExpiresAt,
        CancellationToken cancellationToken)
    {
        var session = await dbContext.AdminSessions.SingleAsync(
            candidate => candidate.Id == sessionId,
            cancellationToken);
        session.LastSeenAt = lastSeenAt;
        session.IdleExpiresAt = idleExpiresAt;

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<bool> RecordSuccessfulStepUpAsync(
        Guid sessionId,
        DateTimeOffset occurredAt,
        DateTimeOffset idleExpiresAt,
        long? acceptedTimeStep,
        AdminAuditEntry auditEntry,
        CancellationToken cancellationToken)
    {
        return await SaveWithRetryAsync(
            async () =>
            {
                var session = await dbContext.AdminSessions.SingleAsync(
                    candidate => candidate.Id == sessionId,
                    cancellationToken);
                if (acceptedTimeStep is not null)
                {
                    if (!await AcceptSecondFactorAsync(session.AdminAccountId, occurredAt, acceptedTimeStep, cancellationToken))
                    {
                        dbContext.ChangeTracker.Clear();
                        return false;
                    }

                    session.MfaAuthenticatedAt ??= occurredAt;
                }

                session.StepUpAuthenticatedAt = occurredAt;
                session.LastSeenAt = occurredAt;
                session.IdleExpiresAt = idleExpiresAt;

                AddAuditEntry(auditEntry);
                await dbContext.SaveChangesAsync(cancellationToken);
                return true;
            },
            cancellationToken);
    }

    public async Task<AdminFailedAttemptOutcome> RecordFailedSecondFactorAsync(
        Guid adminAccountId,
        DateTimeOffset occurredAt,
        int maxFailedAttempts,
        TimeSpan lockoutDuration,
        Func<AdminFailedAttemptOutcome, AdminAuditEntry?> createAuditEntry,
        CancellationToken cancellationToken)
    {
        return await SaveWithRetryAsync(
            async () =>
            {
                var adminAccount = await dbContext.AdminAccounts.SingleAsync(
                    account => account.Id == adminAccountId,
                    cancellationToken);
                adminAccount.FailedSecondFactorAttemptCount++;
                var outcome = new AdminFailedAttemptOutcome(
                    adminAccount.FailedSecondFactorAttemptCount,
                    adminAccount.FailedSecondFactorAttemptCount >= maxFailedAttempts);
                if (outcome.LimitReached)
                {
                    adminAccount.SecondFactorLockedUntil = occurredAt.Add(lockoutDuration);
                }

                adminAccount.UpdatedAt = occurredAt;
                adminAccount.Version++;

                var auditEntry = createAuditEntry(outcome);
                if (auditEntry is not null)
                {
                    AddAuditEntry(auditEntry);
                }

                await dbContext.SaveChangesAsync(cancellationToken);
                return outcome;
            },
            cancellationToken);
    }

    public async Task RecordFailedStepUpAsync(
        AdminAuditEntry auditEntry,
        CancellationToken cancellationToken)
    {
        AddAuditEntry(auditEntry);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task ReplaceRecoveryCodesAsync(
        Guid adminAccountId,
        DateTimeOffset occurredAt,
        IReadOnlyList<AdminRecoveryCodeDraft> recoveryCodes,
        AdminAuditEntry generatedAuditEntry,
        AdminAuditEntry revokedAuditEntry,
        CancellationToken cancellationToken)
    {
        await SaveWithRetryAsync(
            async () =>
            {
                var activeCodes = await dbContext.AdminRecoveryCodes
                    .Where(code => code.AdminAccountId == adminAccountId && code.Status == "active")
                    .ToListAsync(cancellationToken);
                foreach (var activeCode in activeCodes)
                {
                    activeCode.Status = "revoked";
                    activeCode.RevokedAt = occurredAt;
                    activeCode.Version++;
                }

                foreach (var recoveryCode in recoveryCodes)
                {
                    dbContext.AdminRecoveryCodes.Add(new AdminRecoveryCodeRecord
                    {
                        Id = recoveryCode.Id,
                        AdminAccountId = recoveryCode.AdminAccountId,
                        CodeHash = recoveryCode.CodeHash,
                        Status = "active",
                        CreatedAt = recoveryCode.CreatedAt,
                    });
                }

                if (activeCodes.Count > 0)
                {
                    AddAuditEntry(revokedAuditEntry);
                }

                AddAuditEntry(generatedAuditEntry);
                await dbContext.SaveChangesAsync(cancellationToken);
                return true;
            },
            cancellationToken);
    }

    public async Task RecordSecurityAuditAsync(
        AdminAuditEntry auditEntry,
        CancellationToken cancellationToken)
    {
        AddAuditEntry(auditEntry);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task RevokeSessionAsync(
        Guid sessionId,
        DateTimeOffset revokedAt,
        AdminAuditEntry auditEntry,
        CancellationToken cancellationToken)
    {
        var session = await dbContext.AdminSessions.SingleAsync(
            candidate => candidate.Id == sessionId,
            cancellationToken);
        session.RevokedAt ??= revokedAt;

        AddAuditEntry(auditEntry);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<int> RevokeSessionsWithoutSecondFactorAsync(
        Guid adminAccountId,
        Guid currentSessionId,
        DateTimeOffset revokedAt,
        AdminAuditEntry auditEntry,
        CancellationToken cancellationToken)
    {
        var sessions = await dbContext.AdminSessions
            .Where(session =>
                session.AdminAccountId == adminAccountId &&
                session.Id != currentSessionId &&
                session.MfaAuthenticatedAt == null &&
                session.RevokedAt == null &&
                session.ExpiresAt > revokedAt)
            .ToListAsync(cancellationToken);
        if (sessions.Count == 0)
        {
            return 0;
        }

        foreach (var session in sessions)
        {
            session.RevokedAt = revokedAt;
        }

        AddAuditEntry(auditEntry);
        await dbContext.SaveChangesAsync(cancellationToken);
        return sessions.Count;
    }

    private async Task ResetPasswordFailuresAsync(
        Guid adminAccountId,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken)
    {
        var adminAccount = await dbContext.AdminAccounts.SingleAsync(
            account => account.Id == adminAccountId,
            cancellationToken);
        adminAccount.FailedPasswordAttemptCount = 0;
        adminAccount.LockedUntil = null;
        adminAccount.LastPasswordVerifiedAt = occurredAt;
        adminAccount.UpdatedAt = occurredAt;
        adminAccount.Version++;
    }

    /// <summary>
    /// Records a verified second factor on the account: the TOTP step it used,
    /// if any, and a cleared failure count. Returns false when the step is not
    /// later than the last one accepted, which is how a code observed once is
    /// kept from working again within its validity window.
    /// </summary>
    private async Task<bool> AcceptSecondFactorAsync(
        Guid adminAccountId,
        DateTimeOffset occurredAt,
        long? acceptedTimeStep,
        CancellationToken cancellationToken)
    {
        var adminAccount = await dbContext.AdminAccounts.SingleAsync(
            account => account.Id == adminAccountId,
            cancellationToken);
        if (acceptedTimeStep is not null)
        {
            if (adminAccount.LastTotpTimeStep is not null &&
                acceptedTimeStep.Value <= adminAccount.LastTotpTimeStep.Value)
            {
                return false;
            }

            adminAccount.LastTotpTimeStep = acceptedTimeStep;
        }

        adminAccount.FailedSecondFactorAttemptCount = 0;
        adminAccount.SecondFactorLockedUntil = null;
        adminAccount.UpdatedAt = occurredAt;
        adminAccount.Version++;
        return true;
    }

    /// <summary>
    /// Marks the challenge consumed, or returns false with nothing tracked when
    /// it was already consumed or has expired.
    /// </summary>
    private async Task<bool> ConsumeChallengeAsync(
        Guid challengeId,
        DateTimeOffset consumedAt,
        CancellationToken cancellationToken)
    {
        var challenge = await dbContext.AdminLoginChallenges.SingleAsync(
            candidate => candidate.Id == challengeId,
            cancellationToken);
        if (challenge.ConsumedAt is not null || challenge.ExpiresAt <= consumedAt)
        {
            dbContext.ChangeTracker.Clear();
            return false;
        }

        challenge.ConsumedAt = consumedAt;
        challenge.Version++;
        return true;
    }

    /// <summary>
    /// Runs <paramref name="attempt"/> and, when its save loses a concurrency
    /// race, discards what it tracked and runs it again against fresh rows.
    /// </summary>
    private async Task<T> SaveWithRetryAsync<T>(
        Func<Task<T>> attempt,
        CancellationToken cancellationToken)
    {
        for (var attemptNumber = 1; ; attemptNumber++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return await attempt();
            }
            catch (DbUpdateConcurrencyException) when (attemptNumber < MaxConcurrencyAttempts)
            {
                dbContext.ChangeTracker.Clear();
            }
        }
    }

    private void AddSession(AdminSessionDraft session)
    {
        dbContext.AdminSessions.Add(new AdminSessionRecord
        {
            Id = session.Id,
            AdminAccountId = session.AdminAccountId,
            TokenHash = session.TokenHash,
            CreatedAt = session.CreatedAt,
            LastSeenAt = session.LastSeenAt,
            ExpiresAt = session.ExpiresAt,
            IdleExpiresAt = session.IdleExpiresAt,
            MfaAuthenticatedAt = session.MfaAuthenticatedAt,
            StepUpAuthenticatedAt = session.StepUpAuthenticatedAt,
        });
    }

    private void AddAuditEntry(AdminAuditEntry auditEntry)
    {
        dbContext.AuditLogEntries.Add(new AuditLogEntryRecord
        {
            ProjectId = auditEntry.ProjectId,
            EventId = auditEntry.EventId,
            OccurredAt = auditEntry.OccurredAt,
            EventType = auditEntry.EventType,
            Outcome = auditEntry.Outcome,
            ActorType = auditEntry.ActorType,
            ActorId = auditEntry.ActorId,
            SourceService = auditEntry.SourceService,
            SourceIp = auditEntry.SourceIp,
            UserAgent = auditEntry.UserAgent,
            CorrelationId = auditEntry.CorrelationId,
            ReasonCode = auditEntry.ReasonCode,
            SubjectType = auditEntry.SubjectType,
            SubjectId = auditEntry.SubjectId,
        });
    }
}
