using System.Data;
using System.Security.Cryptography;
using Payaffe.Application.Admin;
using Payaffe.Application.Payments;
using Payaffe.Infrastructure.Persistence;
using Payaffe.Infrastructure.Persistence.Records;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Payaffe.Migrations;

public sealed class AdminBootstrapService(
    PayaffeDbContext dbContext,
    IAdminPasswordHasher passwordHasher,
    IClock clock,
    IOptions<AdminAuthenticationOptions> options)
{
    /// <summary>
    /// Sixteen characters, because without a second factor (ADR 0028) the
    /// password may be the only credential this account ever has.
    /// </summary>
    public const int MinimumPasswordLength = 16;

    private const string RecoveryCodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    private const long BootstrapAdvisoryLockId = 0x44565A504159;
    private readonly AdminAuthenticationOptions _options = options.Value;

    public async Task<AdminBootstrapResult> BootstrapAsync(
        AdminBootstrapRequest request,
        CancellationToken cancellationToken)
    {
        var occurredAt = clock.UtcNow;
        var username = request.Username.Trim();
        var normalizedUsername = username.ToUpperInvariant();
        var correlationId = string.IsNullOrWhiteSpace(request.CorrelationId)
            ? Guid.NewGuid().ToString("D")
            : request.CorrelationId.Trim();

        if (string.IsNullOrWhiteSpace(username) ||
            string.IsNullOrEmpty(request.Password))
        {
            await RecordFailureAsync(
                occurredAt,
                correlationId,
                "admin_bootstrap.invalid_input",
                cancellationToken);
            return AdminBootstrapResult.InvalidInput();
        }

        if (request.Password.Length < MinimumPasswordLength)
        {
            await RecordFailureAsync(
                occurredAt,
                correlationId,
                "admin_bootstrap.password_too_short",
                cancellationToken);
            return AdminBootstrapResult.InvalidInput();
        }

        var accountId = Guid.NewGuid();
        var recoveryCodeCount = Math.Clamp(_options.RecoveryCodeCount, 1, 20);
        var plainRecoveryCodes = Enumerable.Range(0, recoveryCodeCount)
            .Select(_ => GenerateRecoveryCode())
            .ToArray();

        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken);
        await dbContext.Database.ExecuteSqlRawAsync(
            $"select pg_advisory_xact_lock({BootstrapAdvisoryLockId})",
            cancellationToken);

        if (await dbContext.AdminAccounts.AnyAsync(cancellationToken))
        {
            dbContext.AuditLogEntries.Add(CreateAuditEntry(
                occurredAt,
                correlationId,
                "denied",
                "admin_bootstrap.already_completed",
                "unknown"));
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return AdminBootstrapResult.AlreadyBootstrapped();
        }

        dbContext.AdminAccounts.Add(new AdminAccountRecord
        {
            Id = accountId,
            Username = username,
            NormalizedUsername = normalizedUsername,
            PasswordHash = passwordHasher.HashPassword(request.Password),
            // No second factor yet. Enrolling one is the admin's own, later
            // (ADR 0028); until then this account signs in on its password.
            TotpSecretReference = null,
            Status = "active",
            FailedPasswordAttemptCount = 0,
            CreatedAt = occurredAt,
            UpdatedAt = occurredAt,
        });
        dbContext.AdminRecoveryCodes.AddRange(plainRecoveryCodes.Select(code =>
            new AdminRecoveryCodeRecord
            {
                Id = Guid.NewGuid(),
                AdminAccountId = accountId,
                CodeHash = passwordHasher.HashPassword(code),
                Status = "active",
                CreatedAt = occurredAt,
            }));
        dbContext.AuditLogEntries.Add(CreateAuditEntry(
            occurredAt,
            correlationId,
            "success",
            "admin_bootstrap.created",
            accountId.ToString("D")));

        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return AdminBootstrapResult.Created(accountId, plainRecoveryCodes);
    }

    private async Task RecordFailureAsync(
        DateTimeOffset occurredAt,
        string correlationId,
        string reasonCode,
        CancellationToken cancellationToken)
    {
        dbContext.AuditLogEntries.Add(CreateAuditEntry(
            occurredAt,
            correlationId,
            "failure",
            reasonCode,
            "unknown"));
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private static AuditLogEntryRecord CreateAuditEntry(
        DateTimeOffset occurredAt,
        string correlationId,
        string outcome,
        string reasonCode,
        string subjectId) =>
        new()
        {
            EventId = Guid.NewGuid(),
            OccurredAt = occurredAt,
            EventType = "admin_account.bootstrap",
            Outcome = outcome,
            ActorType = "system",
            ActorId = "local_operator",
            SourceService = "operations",
            CorrelationId = correlationId,
            ReasonCode = reasonCode,
            SubjectType = "admin_account",
            SubjectId = subjectId,
        };

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
