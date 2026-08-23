using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Payaffe.Infrastructure.Persistence;

/// <summary>
/// Brings the schema up to date in the background while the host starts
/// (ADR 0027).
/// </summary>
/// <remarks>
/// Deliberately not blocking <see cref="StartAsync"/>. Doing so would mean a
/// database that is a few seconds late — which on a first
/// `docker compose up` it routinely is — stops the host from starting at all,
/// taking `/health/live` with it and turning a wait into a crash loop. So the
/// host starts, `/health/ready` answers `not_ready` until this finishes, and
/// the scheduled workers wait on
/// <see cref="SchemaMigrationState.Completion"/>.
/// </remarks>
public sealed class SchemaMigrationHostedService(
    IServiceProvider serviceProvider,
    SchemaMigrationState state,
    IHostApplicationLifetime lifetime,
    ILogger<SchemaMigrationHostedService> logger) : IHostedService
{
    /// <summary>
    /// Exit code 70 is `EX_SOFTWARE`: the host was started against a database
    /// it cannot bring to the schema this build needs.
    /// </summary>
    private const int MigrationFailedExitCode = 70;

    private static readonly TimeSpan FirstRetryDelay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MaximumRetryDelay = TimeSpan.FromSeconds(30);

    private readonly CancellationTokenSource _stopping = new();
    private Task _migration = Task.CompletedTask;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _migration = Task.Run(() => RunAsync(_stopping.Token), CancellationToken.None);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _stopping.CancelAsync();
        try
        {
            await _migration.WaitAsync(cancellationToken);
        }
        catch (Exception)
        {
            // Already logged and already reported through the state. A host
            // that is stopping has nothing left to do about it.
        }
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        state.Applying();
        try
        {
            await WaitForDatabaseAsync(cancellationToken);

            var applied = await SchemaMigrator.ApplyAsync(serviceProvider, cancellationToken);
            state.Complete();

            if (applied.Count > 0)
            {
                logger.LogInformation(
                    "Applied {MigrationCount} database migrations: {Migrations}.",
                    applied.Count,
                    string.Join(", ", applied));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The host is stopping. Not a failure, and not a reason to bring
            // the process down on its way out.
        }
        catch (Exception exception)
        {
            state.Fail(exception);
            logger.LogCritical(
                exception,
                "Applying database migrations failed; this host cannot serve and is stopping.");

            // A migration that cannot apply is a deployment that must not be
            // left half-running. The container restarts into the same failure
            // until somebody looks, which is the cost ADR 0027 accepts.
            Environment.ExitCode = MigrationFailedExitCode;
            lifetime.StopApplication();
        }
    }

    /// <summary>
    /// Waits for PostgreSQL to answer, rather than failing the first time it
    /// does not.
    /// </summary>
    /// <remarks>
    /// A database that is not up yet and a migration that cannot apply are
    /// different failures and get different responses. Only the second one is
    /// fatal; this loop is the first one, and it keeps waiting because
    /// something else — an operator, or the deployment's health gate — is
    /// already watching how long readiness takes.
    /// </remarks>
    private async Task WaitForDatabaseAsync(CancellationToken cancellationToken)
    {
        var delay = FirstRetryDelay;
        var reported = false;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await using var scope = serviceProvider.CreateAsyncScope();
                var database = scope.ServiceProvider.GetRequiredService<PayaffeDbContext>().Database;
                if (await database.CanConnectAsync(cancellationToken))
                {
                    return;
                }
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                if (!reported)
                {
                    logger.LogWarning(
                        exception,
                        "The product database is not reachable yet; waiting before applying migrations.");
                    reported = true;
                }
            }

            await Task.Delay(delay, cancellationToken);
            delay = delay < MaximumRetryDelay
                ? TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, MaximumRetryDelay.Ticks))
                : MaximumRetryDelay;
        }
    }
}
