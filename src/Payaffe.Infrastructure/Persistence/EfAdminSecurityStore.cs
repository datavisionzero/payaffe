using Payaffe.Application.Admin;
using Payaffe.Infrastructure.Persistence.Records;
using Microsoft.EntityFrameworkCore;

namespace Payaffe.Infrastructure.Persistence;

public sealed class EfAdminSecurityStore(PayaffeDbContext dbContext) : IAdminSecurityStore
{
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
        var adminAccount = await dbContext.AdminAccounts.SingleAsync(
            account => account.Id == adminAccountId,
            cancellationToken);
        adminAccount.FailedPasswordAttemptCount = 0;
        adminAccount.LockedUntil = null;
        adminAccount.LastPasswordVerifiedAt = occurredAt;
        adminAccount.UpdatedAt = occurredAt;

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
    }

    public async Task RecordFailedPasswordVerificationAsync(
        Guid? adminAccountId,
        DateTimeOffset occurredAt,
        int? failedPasswordAttemptCount,
        DateTimeOffset? lockedUntil,
        AdminAuditEntry auditEntry,
        CancellationToken cancellationToken)
    {
        if (adminAccountId is not null)
        {
            var adminAccount = await dbContext.AdminAccounts.SingleAsync(
                account => account.Id == adminAccountId,
                cancellationToken);
            if (failedPasswordAttemptCount is not null)
            {
                adminAccount.FailedPasswordAttemptCount = failedPasswordAttemptCount.Value;
            }

            adminAccount.LockedUntil = lockedUntil;
            adminAccount.UpdatedAt = occurredAt;
        }

        AddAuditEntry(auditEntry);
        await dbContext.SaveChangesAsync(cancellationToken);
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
                    challenge.ConsumedAt))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task RecordFailedMfaVerificationAsync(
        Guid? challengeId,
        DateTimeOffset occurredAt,
        int? failedAttemptCount,
        DateTimeOffset? consumedAt,
        AdminAuditEntry auditEntry,
        CancellationToken cancellationToken)
    {
        if (challengeId is not null)
        {
            var challenge = await dbContext.AdminLoginChallenges.SingleAsync(
                candidate => candidate.Id == challengeId,
                cancellationToken);
            if (failedAttemptCount is not null)
            {
                challenge.FailedAttemptCount = failedAttemptCount.Value;
            }

            if (consumedAt is not null)
            {
                challenge.ConsumedAt = consumedAt;
            }
        }

        AddAuditEntry(auditEntry);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task CompleteMfaVerificationAsync(
        Guid challengeId,
        DateTimeOffset consumedAt,
        AdminSessionDraft session,
        AdminAuditEntry auditEntry,
        CancellationToken cancellationToken)
    {
        var challenge = await dbContext.AdminLoginChallenges.SingleAsync(
            candidate => candidate.Id == challengeId,
            cancellationToken);
        challenge.ConsumedAt = consumedAt;

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

        AddAuditEntry(auditEntry);
        await dbContext.SaveChangesAsync(cancellationToken);
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

    public async Task CompleteMfaVerificationWithRecoveryCodeAsync(
        Guid challengeId,
        Guid recoveryCodeId,
        DateTimeOffset consumedAt,
        AdminSessionDraft session,
        AdminAuditEntry mfaAuditEntry,
        AdminAuditEntry recoveryCodeAuditEntry,
        CancellationToken cancellationToken)
    {
        var challenge = await dbContext.AdminLoginChallenges.SingleAsync(
            candidate => candidate.Id == challengeId,
            cancellationToken);
        challenge.ConsumedAt = consumedAt;

        var recoveryCode = await dbContext.AdminRecoveryCodes.SingleAsync(
            candidate => candidate.Id == recoveryCodeId,
            cancellationToken);
        recoveryCode.Status = "used";
        recoveryCode.UsedAt = consumedAt;

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

        AddAuditEntry(mfaAuditEntry);
        AddAuditEntry(recoveryCodeAuditEntry);
        await dbContext.SaveChangesAsync(cancellationToken);
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
                    session.RevokedAt))
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

    public async Task RecordSuccessfulStepUpAsync(
        Guid sessionId,
        DateTimeOffset occurredAt,
        DateTimeOffset idleExpiresAt,
        AdminAuditEntry auditEntry,
        CancellationToken cancellationToken)
    {
        var session = await dbContext.AdminSessions.SingleAsync(
            candidate => candidate.Id == sessionId,
            cancellationToken);
        session.StepUpAuthenticatedAt = occurredAt;
        session.LastSeenAt = occurredAt;
        session.IdleExpiresAt = idleExpiresAt;

        AddAuditEntry(auditEntry);
        await dbContext.SaveChangesAsync(cancellationToken);
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
        var activeCodes = await dbContext.AdminRecoveryCodes
            .Where(code => code.AdminAccountId == adminAccountId && code.Status == "active")
            .ToListAsync(cancellationToken);
        foreach (var activeCode in activeCodes)
        {
            activeCode.Status = "revoked";
            activeCode.RevokedAt = occurredAt;
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

    private void AddAuditEntry(AdminAuditEntry auditEntry)
    {
        dbContext.AuditLogEntries.Add(new AuditLogEntryRecord
        {
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
