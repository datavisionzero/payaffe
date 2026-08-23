using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Payaffe.Infrastructure.Persistence;

/// <summary>
/// Brings the product database up to the schema this build expects.
/// </summary>
/// <remarks>
/// This runs while the <c>api</c> and <c>worker</c> hosts start
/// (ADR 0027). Both of them need the schema and either may come up first, so
/// both apply it and the advisory lock below is what keeps that from being a
/// race. The <c>migrations</c> host calls the same code for the manual run.
/// </remarks>
public static class SchemaMigrator
{
    /// <summary>
    /// The key every payaffe host locks the schema with. Its bytes are ASCII
    /// <c>payaffe</c> and a version byte, so a value that collides with an
    /// unrelated application's advisory lock on a shared database is not a
    /// thing that happens by accident.
    /// </summary>
    private const long SchemaAdvisoryLockKey = 0x7061796166666501L;

    /// <returns>
    /// The migrations this call applied, in order. Empty when the schema was
    /// already current, which is the ordinary case for every start after the
    /// first one of a version.
    /// </returns>
    public static async Task<IReadOnlyList<string>> ApplyAsync(
        IServiceProvider serviceProvider,
        CancellationToken cancellationToken)
    {
        await using var scope = serviceProvider.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<PayaffeDbContext>().Database;

        // Opened here rather than left to EF Core, because the lock has to
        // outlive the statement that takes it: `pg_advisory_lock` is held for
        // the session, and a session EF opens and closes per command would
        // release it before the migration it is meant to guard. EF reuses a
        // connection that is already open, so everything below runs on this
        // one.
        await database.OpenConnectionAsync(cancellationToken);
        try
        {
            // Blocks rather than failing when the other host got here first.
            // Whoever wins migrates; the loser waits, finds nothing pending
            // and carries on. That is why the two hosts need no ordering
            // between them and no `depends_on` beyond the database.
            await database.ExecuteSqlRawAsync(
                $"select pg_advisory_lock({SchemaAdvisoryLockKey})",
                cancellationToken);
            try
            {
                var pending = (await database.GetPendingMigrationsAsync(cancellationToken)).ToArray();
                if (pending.Length > 0)
                {
                    await database.MigrateAsync(cancellationToken);
                }

                return pending;
            }
            finally
            {
                await database.ExecuteSqlRawAsync(
                    $"select pg_advisory_unlock({SchemaAdvisoryLockKey})",
                    CancellationToken.None);
            }
        }
        finally
        {
            await database.CloseConnectionAsync();
        }
    }
}
