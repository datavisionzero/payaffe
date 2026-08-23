using Microsoft.Extensions.Hosting;

namespace Payaffe.Infrastructure.Persistence;

/// <summary>
/// A scheduled worker that does not tick until the schema it needs is in place.
/// </summary>
/// <remarks>
/// The hosts migrate as they start (ADR 0027), so on a cold start there is a
/// window in which the tables a worker reads do not exist yet. Ticking into it
/// would produce a burst of failures on every first start that mean nothing —
/// and an error is an entry ([ADR 0026](../../../docs/adr/0026-an-error-is-an-entry-and-there-is-no-error-tracker.md)),
/// so those entries would be indistinguishable from the ones worth reading.
/// The gate lives here rather than in each worker so that a worker added later
/// cannot forget it.
/// </remarks>
public abstract class SchemaGatedBackgroundService(SchemaMigrationState schemaMigration) : BackgroundService
{
    protected sealed override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Faults if the migration failed, which stops this host — the same
        // outcome the migration itself is heading for, reached without a
        // worker having done anything against a schema it cannot trust.
        await schemaMigration.Completion.WaitAsync(stoppingToken);
        await RunAsync(stoppingToken);
    }

    protected abstract Task RunAsync(CancellationToken stoppingToken);
}
