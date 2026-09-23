using System.Security.Cryptography;
using Payaffe.Application.Payments;
using Microsoft.Extensions.Options;

namespace Payaffe.Application.Admin;

public sealed class AdminAuthenticationService(
    IAdminSecurityStore store,
    IAdminPasswordHasher passwordHasher,
    IAdminTotpSecretResolver totpSecretResolver,
    IAdminTotpVerifier totpVerifier,
    IAdminSessionTokenService sessionTokenService,
    IClock clock,
    IOptions<AdminAuthenticationOptions> options)
{
    private const string RecoveryCodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    private readonly AdminAuthenticationOptions _options = options.Value;

    public async Task<AdminLoginStartResult> StartLoginAsync(
        AdminLoginStartCommand command,
        CancellationToken cancellationToken)
    {
        var occurredAt = clock.UtcNow;
        var normalizedUsername = NormalizeUsername(command.Username);
        if (normalizedUsername is null || string.IsNullOrEmpty(command.Password))
        {
            await RecordFailureAsync(
                adminAccount: null,
                occurredAt,
                command,
                "admin_login.invalid_credentials",
                cancellationToken);
            return AdminLoginStartResult.InvalidCredentials();
        }

        var adminAccount = await store.FindByNormalizedUsernameAsync(normalizedUsername, cancellationToken);
        if (adminAccount is null)
        {
            await RecordFailureAsync(
                adminAccount: null,
                occurredAt,
                command,
                "admin_login.invalid_credentials",
                cancellationToken);
            return AdminLoginStartResult.InvalidCredentials();
        }

        if (adminAccount.Status != "active")
        {
            await RecordFailureAsync(
                adminAccount,
                occurredAt,
                command,
                "admin_login.account_disabled",
                cancellationToken);
            return AdminLoginStartResult.InvalidCredentials();
        }

        if (adminAccount.LockedUntil is not null && adminAccount.LockedUntil > occurredAt)
        {
            await RecordFailureAsync(
                adminAccount,
                occurredAt,
                command,
                "admin_login.account_locked",
                cancellationToken);
            return AdminLoginStartResult.InvalidCredentials();
        }

        if (!passwordHasher.VerifyPassword(command.Password, adminAccount.PasswordHash))
        {
            await store.RecordFailedPasswordVerificationAsync(
                adminAccount.Id,
                occurredAt,
                _options.MaxFailedPasswordAttempts,
                _options.LockoutDuration,
                outcome => CreateLoginFailureAuditEntry(
                    adminAccount,
                    occurredAt,
                    command,
                    outcome.LimitReached ? "admin_login.account_locked" : "admin_login.invalid_credentials"),
                cancellationToken);
            return AdminLoginStartResult.InvalidCredentials();
        }

        // No second factor enrolled: the password was the whole sign-in
        // (ADR 0028). This used to be refused as invalid credentials, which
        // made an account without TOTP unusable rather than optional.
        //
        // The session is the same one the second step would have issued, with
        // one difference that matters: MfaAuthenticatedAt stays null, so
        // anything that asks whether this session cleared a second factor gets
        // an honest no.
        if (string.IsNullOrWhiteSpace(adminAccount.TotpSecretReference))
        {
            var passwordOnlyToken = sessionTokenService.GenerateToken();
            var passwordOnlySession = CreateSessionDraft(
                adminAccount.Id,
                passwordOnlyToken,
                occurredAt) with
            {
                MfaAuthenticatedAt = null,
                StepUpAuthenticatedAt = null,
            };

            await store.RecordPasswordOnlyAuthenticationAsync(
                adminAccount.Id,
                occurredAt,
                passwordOnlySession,
                CreateAuditEntry(
                    "admin.login_complete",
                    occurredAt,
                    command,
                    outcome: "success",
                    actorType: "product_user",
                    actorId: adminAccount.Id.ToString("D"),
                    reasonCode: "admin_login.password_only",
                    subjectId: passwordOnlySession.Id.ToString("D"),
                    subjectType: "admin_session"),
                cancellationToken);

            return AdminLoginStartResult.Authenticated(
                passwordOnlyToken,
                passwordOnlySession.ExpiresAt);
        }

        var loginChallenge = new AdminLoginChallengeDraft(
            Guid.NewGuid(),
            adminAccount.Id,
            occurredAt.Add(_options.LoginChallengeLifetime),
            occurredAt);

        await store.RecordSuccessfulPasswordVerificationAsync(
            adminAccount.Id,
            occurredAt,
            loginChallenge,
            CreateAuditEntry(
                "admin.login_start",
                occurredAt,
                command,
                outcome: "success",
                actorType: "product_user",
                actorId: adminAccount.Id.ToString("D"),
                reasonCode: "admin_login.password_verified",
                subjectId: adminAccount.Id.ToString("D")),
            cancellationToken);

        return AdminLoginStartResult.MfaRequired(loginChallenge.Id);
    }

    public async Task<AdminMfaCompleteResult> CompleteMfaAsync(
        AdminMfaCompleteCommand command,
        CancellationToken cancellationToken)
    {
        var occurredAt = clock.UtcNow;
        var hasTotpCode = !string.IsNullOrWhiteSpace(command.TotpCode);
        var hasRecoveryCode = !string.IsNullOrWhiteSpace(command.RecoveryCode);
        if (command.ChallengeId is null || hasTotpCode == hasRecoveryCode)
        {
            await RecordMfaFailureAsync(
                challenge: null,
                occurredAt,
                command,
                "admin_mfa.invalid",
                cancellationToken);
            return AdminMfaCompleteResult.Invalid();
        }

        var challenge = await store.FindLoginChallengeAsync(command.ChallengeId.Value, cancellationToken);
        if (challenge is null)
        {
            await RecordMfaFailureAsync(
                challenge: null,
                occurredAt,
                command,
                "admin_mfa.invalid",
                cancellationToken);
            return AdminMfaCompleteResult.Invalid();
        }

        if (challenge.ConsumedAt is not null ||
            challenge.ExpiresAt <= occurredAt ||
            challenge.AdminAccountStatus != "active")
        {
            await RecordMfaFailureAsync(
                challenge,
                occurredAt,
                command,
                challenge.ExpiresAt <= occurredAt ? "admin_mfa.challenge_expired" : "admin_mfa.invalid",
                cancellationToken);
            return AdminMfaCompleteResult.Invalid();
        }

        if (hasTotpCode)
        {
            var secret = await totpSecretResolver.ResolveSecretAsync(challenge.TotpSecretReference, cancellationToken);
            if (secret is not null &&
                totpVerifier.VerifyCode(secret, command.TotpCode!, occurredAt, _options.TotpAllowedTimeStepSkew))
            {
                var sessionToken = sessionTokenService.GenerateToken();
                var session = CreateSessionDraft(challenge.AdminAccountId, sessionToken, occurredAt);

                var completed = await store.CompleteMfaVerificationAsync(
                    challenge.Id,
                    occurredAt,
                    session,
                    CreateAuditEntry(
                        "admin.mfa_complete",
                        occurredAt,
                        command,
                        outcome: "success",
                        actorType: "product_user",
                        actorId: challenge.AdminAccountId.ToString("D"),
                        reasonCode: "admin_mfa.verified",
                        subjectType: "admin_session",
                        subjectId: session.Id.ToString("D")),
                    cancellationToken);
                if (!completed)
                {
                    // A parallel request used the challenge or the Recovery
                    // Code first; single use means this one fails.
                    await RecordMfaFailureAsync(challenge, occurredAt, command, "admin_mfa.invalid", cancellationToken);
                    return AdminMfaCompleteResult.Invalid();
                }

                await RevokeSessionsWithoutSecondFactorAsync(
                    challenge.AdminAccountId,
                    session.Id,
                    occurredAt,
                    command.SourceIp,
                    command.UserAgent,
                    command.CorrelationId,
                    cancellationToken);
                return AdminMfaCompleteResult.Authenticated(sessionToken, session.ExpiresAt);
            }
        }
        else
        {
            var normalizedRecoveryCode = NormalizeRecoveryCode(command.RecoveryCode);
            var matchingRecoveryCode = await FindMatchingRecoveryCodeAsync(
                challenge.AdminAccountId,
                normalizedRecoveryCode,
                cancellationToken);
            if (matchingRecoveryCode is not null)
            {
                var sessionToken = sessionTokenService.GenerateToken();
                var session = CreateSessionDraft(challenge.AdminAccountId, sessionToken, occurredAt);

                var completed = await store.CompleteMfaVerificationWithRecoveryCodeAsync(
                    challenge.Id,
                    matchingRecoveryCode.Id,
                    occurredAt,
                    session,
                    CreateAuditEntry(
                        "admin.mfa_complete",
                        occurredAt,
                        command,
                        outcome: "success",
                        actorType: "product_user",
                        actorId: challenge.AdminAccountId.ToString("D"),
                        reasonCode: "admin_mfa.recovery_code_verified",
                        subjectType: "admin_session",
                        subjectId: session.Id.ToString("D")),
                    CreateAuditEntry(
                        "admin.recovery_codes.use",
                        occurredAt,
                        command,
                        outcome: "success",
                        actorType: "product_user",
                        actorId: challenge.AdminAccountId.ToString("D"),
                        reasonCode: "admin_recovery_codes.used",
                        subjectType: "admin_account",
                        subjectId: challenge.AdminAccountId.ToString("D")),
                    cancellationToken);
                if (!completed)
                {
                    // A parallel request used the challenge or the Recovery
                    // Code first; single use means this one fails.
                    await RecordMfaFailureAsync(challenge, occurredAt, command, "admin_mfa.invalid", cancellationToken);
                    return AdminMfaCompleteResult.Invalid();
                }

                await RevokeSessionsWithoutSecondFactorAsync(
                    challenge.AdminAccountId,
                    session.Id,
                    occurredAt,
                    command.SourceIp,
                    command.UserAgent,
                    command.CorrelationId,
                    cancellationToken);
                return AdminMfaCompleteResult.Authenticated(sessionToken, session.ExpiresAt);
            }
        }

        await store.RecordFailedMfaVerificationAsync(
            challenge.Id,
            occurredAt,
            _options.MaxFailedMfaAttempts,
            outcome => CreateMfaFailureAuditEntry(
                challenge,
                occurredAt,
                command,
                outcome.LimitReached ? "admin_mfa.challenge_locked" : "admin_mfa.invalid"),
            cancellationToken);
        return AdminMfaCompleteResult.Invalid();
    }

    private async Task<AdminRecoveryCodeReadModel?> FindMatchingRecoveryCodeAsync(
        Guid adminAccountId,
        string recoveryCode,
        CancellationToken cancellationToken)
    {
        var activeRecoveryCodes = await store.FindActiveRecoveryCodesAsync(adminAccountId, cancellationToken);
        return activeRecoveryCodes.FirstOrDefault(activeRecoveryCode =>
            passwordHasher.VerifyPassword(recoveryCode, activeRecoveryCode.CodeHash));
    }

    private AdminSessionDraft CreateSessionDraft(
        Guid adminAccountId,
        string sessionToken,
        DateTimeOffset occurredAt)
    {
        return new AdminSessionDraft(
            Guid.NewGuid(),
            adminAccountId,
            sessionTokenService.HashToken(sessionToken),
            occurredAt,
            occurredAt,
            occurredAt.Add(_options.SessionAbsoluteLifetime),
            occurredAt.Add(_options.SessionIdleLifetime),
            occurredAt,
            occurredAt);
    }

    private static string NormalizeRecoveryCode(string? recoveryCode) =>
        recoveryCode?.Trim().ToUpperInvariant() ?? string.Empty;

    public async Task<AdminSessionAuthenticationResult> AuthenticateSessionAsync(
        string? sessionToken,
        CancellationToken cancellationToken)
    {
        var occurredAt = clock.UtcNow;
        if (string.IsNullOrWhiteSpace(sessionToken))
        {
            return AdminSessionAuthenticationResult.Invalid();
        }

        var tokenHash = sessionTokenService.HashToken(sessionToken);
        var session = await store.FindSessionByTokenHashAsync(tokenHash, cancellationToken);
        if (session is null ||
            session.AdminAccountStatus != "active" ||
            session.RevokedAt is not null ||
            session.ExpiresAt <= occurredAt ||
            session.IdleExpiresAt <= occurredAt)
        {
            return AdminSessionAuthenticationResult.Invalid();
        }

        var refreshedIdleExpiresAt = Min(
            occurredAt.Add(_options.SessionIdleLifetime),
            session.ExpiresAt);
        await store.RefreshSessionAsync(
            session.Id,
            occurredAt,
            refreshedIdleExpiresAt,
            cancellationToken);

        return AdminSessionAuthenticationResult.Authenticated(
            new AdminSessionPrincipal(
                session.AdminAccountId,
                session.Username,
                session.Id,
                session.MfaAuthenticatedAt,
                session.StepUpAuthenticatedAt,
                session.ExpiresAt,
                refreshedIdleExpiresAt,
                HasEnrolledSecondFactor: !string.IsNullOrWhiteSpace(session.TotpSecretReference)));
    }

    public async Task<AdminStepUpResult> StepUpAsync(
        AdminStepUpCommand command,
        CancellationToken cancellationToken)
    {
        var occurredAt = clock.UtcNow;
        if (string.IsNullOrWhiteSpace(command.SessionToken))
        {
            return AdminStepUpResult.Invalid();
        }

        var tokenHash = sessionTokenService.HashToken(command.SessionToken);
        var session = await store.FindSessionByTokenHashAsync(tokenHash, cancellationToken);
        if (session is null ||
            session.AdminAccountStatus != "active" ||
            session.RevokedAt is not null ||
            session.ExpiresAt <= occurredAt ||
            session.IdleExpiresAt <= occurredAt)
        {
            return AdminStepUpResult.Invalid();
        }

        // An account with no second factor has nothing to step up with, and
        // refusing here would make the sensitive operations unreachable for it
        // rather than protected (ADR 0028). An account that did enrol keeps
        // step-up in full: opting in has to be worth something.
        var hasEnrolledSecondFactor = !string.IsNullOrWhiteSpace(session.TotpSecretReference);
        if (hasEnrolledSecondFactor)
        {
            if (string.IsNullOrWhiteSpace(command.TotpCode))
            {
                await RecordStepUpFailureAsync(session, occurredAt, command, "admin_step_up.invalid", cancellationToken);
                return AdminStepUpResult.Invalid();
            }

            var secret = await totpSecretResolver.ResolveSecretAsync(session.TotpSecretReference, cancellationToken);
            if (secret is null ||
                !totpVerifier.VerifyCode(secret, command.TotpCode, occurredAt, _options.TotpAllowedTimeStepSkew))
            {
                await RecordStepUpFailureAsync(session, occurredAt, command, "admin_step_up.invalid", cancellationToken);
                return AdminStepUpResult.Invalid();
            }
        }

        var refreshedIdleExpiresAt = Min(
            occurredAt.Add(_options.SessionIdleLifetime),
            session.ExpiresAt);
        await store.RecordSuccessfulStepUpAsync(
            session.Id,
            occurredAt,
            refreshedIdleExpiresAt,
            secondFactorVerified: hasEnrolledSecondFactor,
            CreateAuditEntry(
                "admin.step_up",
                occurredAt,
                command,
                outcome: "success",
                actorType: "product_user",
                actorId: session.AdminAccountId.ToString("D"),
                reasonCode: "admin_step_up.verified",
                subjectId: session.Id.ToString("D")),
            cancellationToken);

        if (hasEnrolledSecondFactor)
        {
            await RevokeSessionsWithoutSecondFactorAsync(
                session.AdminAccountId,
                session.Id,
                occurredAt,
                command.SourceIp,
                command.UserAgent,
                command.CorrelationId,
                cancellationToken);
        }

        return AdminStepUpResult.Authenticated(occurredAt, refreshedIdleExpiresAt);
    }

    public async Task<AdminRecoveryCodeGenerationResult> GenerateRecoveryCodesAsync(
        AdminSessionPrincipal principal,
        string? sourceIp,
        string? userAgent,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var occurredAt = clock.UtcNow;
        var recoveryCodeCount = Math.Clamp(_options.RecoveryCodeCount, 1, 20);
        var plainCodes = Enumerable
            .Range(0, recoveryCodeCount)
            .Select(_ => GenerateRecoveryCode())
            .ToArray();
        var recoveryCodes = plainCodes
            .Select(code => new AdminRecoveryCodeDraft(
                Guid.NewGuid(),
                principal.AdminAccountId,
                passwordHasher.HashPassword(code),
                occurredAt))
            .ToArray();

        await store.ReplaceRecoveryCodesAsync(
            principal.AdminAccountId,
            occurredAt,
            recoveryCodes,
            new AdminAuditEntry(
                Guid.NewGuid(),
                occurredAt,
                "admin.recovery_codes.generate",
                "success",
                "product_user",
                principal.AdminAccountId.ToString("D"),
                "api",
                sourceIp,
                userAgent,
                correlationId,
                "admin_recovery_codes.generated",
                "admin_account",
                principal.AdminAccountId.ToString("D")),
            new AdminAuditEntry(
                Guid.NewGuid(),
                occurredAt,
                "admin.recovery_codes.revoke",
                "revoked",
                "product_user",
                principal.AdminAccountId.ToString("D"),
                "api",
                sourceIp,
                userAgent,
                correlationId,
                "admin_recovery_codes.rotated",
                "admin_account",
                principal.AdminAccountId.ToString("D")),
            cancellationToken);

        return new AdminRecoveryCodeGenerationResult(occurredAt, plainCodes);
    }

    public async Task RecordSecurityAuditAsync(
        AdminAuditEntry auditEntry,
        CancellationToken cancellationToken)
    {
        await store.RecordSecurityAuditAsync(auditEntry, cancellationToken);
    }

    public async Task<AdminLogoutResult> LogoutAsync(
        AdminLogoutCommand command,
        CancellationToken cancellationToken)
    {
        var occurredAt = clock.UtcNow;
        if (string.IsNullOrWhiteSpace(command.SessionToken))
        {
            return AdminLogoutResult.NoActiveSession();
        }

        var tokenHash = sessionTokenService.HashToken(command.SessionToken);
        var session = await store.FindSessionByTokenHashAsync(tokenHash, cancellationToken);
        if (session is null ||
            session.AdminAccountStatus != "active" ||
            session.RevokedAt is not null ||
            session.ExpiresAt <= occurredAt ||
            session.IdleExpiresAt <= occurredAt)
        {
            return AdminLogoutResult.NoActiveSession();
        }

        await store.RevokeSessionAsync(
            session.Id,
            occurredAt,
            CreateAuditEntry(
                "admin.logout",
                occurredAt,
                command,
                outcome: "success",
                actorType: "product_user",
                actorId: session.AdminAccountId.ToString("D"),
                reasonCode: "admin_logout.session_revoked",
                subjectId: session.Id.ToString("D")),
            cancellationToken);

        return AdminLogoutResult.SessionRevoked();
    }

    /// <summary>
    /// Ends the account's sessions that never cleared a second factor, once the
    /// account has proven one.
    /// </summary>
    /// <remarks>
    /// Enrollment happens outside the product (the account's TOTP reference is
    /// set through the operator procedure), so the first verified factor is the
    /// first moment the product can act on it. A password-only session begun
    /// before enrollment is exactly the one a compromise would have left behind.
    /// </remarks>
    private async Task RevokeSessionsWithoutSecondFactorAsync(
        Guid adminAccountId,
        Guid currentSessionId,
        DateTimeOffset occurredAt,
        string? sourceIp,
        string? userAgent,
        string correlationId,
        CancellationToken cancellationToken)
    {
        await store.RevokeSessionsWithoutSecondFactorAsync(
            adminAccountId,
            currentSessionId,
            occurredAt,
            new AdminAuditEntry(
                Guid.NewGuid(),
                occurredAt,
                "admin.sessions.revoke",
                "revoked",
                "product_user",
                adminAccountId.ToString("D"),
                "api",
                sourceIp,
                userAgent,
                correlationId,
                "admin_session.second_factor_enrolled",
                "admin_account",
                adminAccountId.ToString("D")),
            cancellationToken);
    }

    private async Task RecordFailureAsync(
        AdminAccountReadModel? adminAccount,
        DateTimeOffset occurredAt,
        AdminLoginStartCommand command,
        string reasonCode,
        CancellationToken cancellationToken)
    {
        await store.RecordSecurityAuditAsync(
            CreateLoginFailureAuditEntry(adminAccount, occurredAt, command, reasonCode),
            cancellationToken);
    }

    private static AdminAuditEntry CreateLoginFailureAuditEntry(
        AdminAccountReadModel? adminAccount,
        DateTimeOffset occurredAt,
        AdminLoginStartCommand command,
        string reasonCode) =>
        CreateAuditEntry(
            "admin.login_start",
            occurredAt,
            command,
            outcome: reasonCode == "admin_login.account_locked" ? "denied" : "failure",
            actorType: adminAccount is null ? "system" : "product_user",
            actorId: adminAccount?.Id.ToString("D") ?? "unknown",
            reasonCode,
            subjectId: adminAccount?.Id.ToString("D") ?? "unknown");

    private async Task RecordStepUpFailureAsync(
        AdminSessionReadModel session,
        DateTimeOffset occurredAt,
        AdminStepUpCommand command,
        string reasonCode,
        CancellationToken cancellationToken)
    {
        await store.RecordFailedStepUpAsync(
            CreateAuditEntry(
                "admin.step_up",
                occurredAt,
                command,
                outcome: "denied",
                actorType: "product_user",
                actorId: session.AdminAccountId.ToString("D"),
                reasonCode,
                subjectId: session.Id.ToString("D")),
            cancellationToken);
    }

    private async Task RecordMfaFailureAsync(
        AdminLoginChallengeReadModel? challenge,
        DateTimeOffset occurredAt,
        AdminMfaCompleteCommand command,
        string reasonCode,
        CancellationToken cancellationToken)
    {
        await store.RecordSecurityAuditAsync(
            CreateMfaFailureAuditEntry(challenge, occurredAt, command, reasonCode),
            cancellationToken);
    }

    private static AdminAuditEntry CreateMfaFailureAuditEntry(
        AdminLoginChallengeReadModel? challenge,
        DateTimeOffset occurredAt,
        AdminMfaCompleteCommand command,
        string reasonCode) =>
        CreateAuditEntry(
            "admin.mfa_complete",
            occurredAt,
            command,
            outcome: reasonCode == "admin_mfa.challenge_locked" ? "denied" : "failure",
            actorType: challenge is null ? "system" : "product_user",
            actorId: challenge?.AdminAccountId.ToString("D") ?? "unknown",
            reasonCode,
            subjectType: challenge is null ? "admin_account" : "admin_login_challenge",
            subjectId: challenge?.Id.ToString("D") ?? "unknown");

    private static AdminAuditEntry CreateAuditEntry(
        string eventType,
        DateTimeOffset occurredAt,
        AdminLoginStartCommand command,
        string outcome,
        string actorType,
        string actorId,
        string reasonCode,
        string subjectId,
        // A password-only sign-in ends at a session rather than at the account,
        // so it names one — the same subject the second step would have named.
        string subjectType = "admin_account")
    {
        return new AdminAuditEntry(
            Guid.NewGuid(),
            occurredAt,
            eventType,
            outcome,
            actorType,
            actorId,
            "api",
            command.SourceIp,
            command.UserAgent,
            command.CorrelationId,
            reasonCode,
            subjectType,
            subjectId);
    }

    private static AdminAuditEntry CreateAuditEntry(
        string eventType,
        DateTimeOffset occurredAt,
        AdminMfaCompleteCommand command,
        string outcome,
        string actorType,
        string actorId,
        string reasonCode,
        string subjectType,
        string subjectId)
    {
        return new AdminAuditEntry(
            Guid.NewGuid(),
            occurredAt,
            eventType,
            outcome,
            actorType,
            actorId,
            "api",
            command.SourceIp,
            command.UserAgent,
            command.CorrelationId,
            reasonCode,
            subjectType,
            subjectId);
    }

    private static AdminAuditEntry CreateAuditEntry(
        string eventType,
        DateTimeOffset occurredAt,
        AdminLogoutCommand command,
        string outcome,
        string actorType,
        string actorId,
        string reasonCode,
        string subjectId)
    {
        return new AdminAuditEntry(
            Guid.NewGuid(),
            occurredAt,
            eventType,
            outcome,
            actorType,
            actorId,
            "api",
            command.SourceIp,
            command.UserAgent,
            command.CorrelationId,
            reasonCode,
            "admin_session",
            subjectId);
    }

    private static AdminAuditEntry CreateAuditEntry(
        string eventType,
        DateTimeOffset occurredAt,
        AdminStepUpCommand command,
        string outcome,
        string actorType,
        string actorId,
        string reasonCode,
        string subjectId)
    {
        return new AdminAuditEntry(
            Guid.NewGuid(),
            occurredAt,
            eventType,
            outcome,
            actorType,
            actorId,
            "api",
            command.SourceIp,
            command.UserAgent,
            command.CorrelationId,
            reasonCode,
            "admin_session",
            subjectId);
    }

    private static string? NormalizeUsername(string? username)
    {
        var trimmedUsername = username?.Trim();
        return string.IsNullOrWhiteSpace(trimmedUsername)
            ? null
            : trimmedUsername.ToUpperInvariant();
    }

    private static DateTimeOffset Min(DateTimeOffset first, DateTimeOffset second) =>
        first <= second ? first : second;

    private static string GenerateRecoveryCode()
    {
        Span<char> characters = stackalloc char[14];
        for (var index = 0; index < characters.Length; index++)
        {
            if (index is 4 or 9)
            {
                characters[index] = '-';
                continue;
            }

            characters[index] = RecoveryCodeAlphabet[
                RandomNumberGenerator.GetInt32(RecoveryCodeAlphabet.Length)];
        }

        return new string(characters);
    }
}
