using Payaffe.Application.Installation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Payaffe.Infrastructure.Persistence;

/// <summary>
/// The Installation Mode as the database remembers it (ADR 0033).
/// </summary>
/// <remarks>
/// The first host to start against an empty database records its configured
/// mode; every host after that must be configured for the same one. That is
/// what keeps a database from ever holding real and simulated Payments at
/// once, which nothing downstream could untangle.
/// </remarks>
public static class InstallationModeRecord
{
    /// <summary>
    /// Records the configured mode if none is recorded yet, and refuses a
    /// configured mode that differs from the recorded one. Runs under the
    /// schema lock, so two hosts starting at once cannot record different
    /// modes.
    /// </summary>
    public static async Task EnsureAsync(
        IServiceProvider serviceProvider,
        CancellationToken cancellationToken)
    {
        var configured = serviceProvider.GetRequiredService<ConfiguredInstallationMode>();
        var dbContext = serviceProvider.GetRequiredService<PayaffeDbContext>();

        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            insert into app.installation (id, mode, recorded_at)
            values (1, {configured.Name}, now())
            on conflict (id) do nothing
            """,
            cancellationToken);

        var recorded = await ReadAsync(dbContext, cancellationToken)
            ?? throw new InvalidOperationException("The Installation Mode could not be recorded.");
        EnsureMatches(configured, recorded);
        Report(serviceProvider, configured);
    }

    /// <summary>
    /// Refuses a configured mode that differs from the recorded one, for a
    /// host that does not apply the schema itself. A database nobody has
    /// migrated yet has nothing recorded and nothing to disagree with.
    /// </summary>
    public static async Task VerifyAsync(
        IServiceProvider serviceProvider,
        CancellationToken cancellationToken)
    {
        var configured = serviceProvider.GetRequiredService<ConfiguredInstallationMode>();
        var dbContext = serviceProvider.GetRequiredService<PayaffeDbContext>();
        var recorded = await ReadAsync(dbContext, cancellationToken);
        if (recorded is not null)
        {
            EnsureMatches(configured, recorded.Value);
        }

        Report(serviceProvider, configured);
    }

    private static async Task<InstallationMode?> ReadAsync(
        PayaffeDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var exists = await dbContext.Database
            .SqlQuery<bool>($"select to_regclass('app.installation') is not null as \"Value\"")
            .SingleAsync(cancellationToken);
        if (!exists)
        {
            return null;
        }

        var name = await dbContext.Database
            .SqlQuery<string>($"select mode as \"Value\" from app.installation where id = 1")
            .SingleOrDefaultAsync(cancellationToken);
        if (name is null)
        {
            return null;
        }

        return ConfiguredInstallationMode.TryParseName(name, out var mode)
            ? mode
            : throw new InvalidOperationException($"The recorded Installation Mode '{name}' is not one this build knows.");
    }

    private static void EnsureMatches(ConfiguredInstallationMode configured, InstallationMode recorded)
    {
        if (configured.Mode != recorded)
        {
            throw new InstallationModeMismatchException(recorded, configured.Mode);
        }
    }

    private static void Report(IServiceProvider serviceProvider, ConfiguredInstallationMode configured)
    {
        var logger = serviceProvider.GetService<ILoggerFactory>()?.CreateLogger(typeof(InstallationModeRecord));
        if (configured.IsTest)
        {
            logger?.LogWarning(
                "This installation is in Test Mode: Payment Addresses, exchange rates and Blockchain Observation are simulated, and no real payment is observed.");
        }
        else
        {
            logger?.LogInformation("This installation is in live mode.");
        }
    }
}

public sealed class InstallationModeMismatchException(InstallationMode recorded, InstallationMode configured)
    : InvalidOperationException(
        $"This installation's database was created in {ConfiguredInstallationMode.ToName(recorded)} mode, " +
        $"but this host is configured for {ConfiguredInstallationMode.ToName(configured)} mode " +
        $"({ConfiguredInstallationMode.ConfigurationKey}). A database never changes mode: run " +
        $"{ConfiguredInstallationMode.ToName(configured)} mode against a new database, or set the configuration back.")
{
    public InstallationMode Recorded { get; } = recorded;

    public InstallationMode Configured { get; } = configured;
}
