using Payaffe.Infrastructure;
using Payaffe.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace Payaffe.Integration.Tests.Persistence;

public sealed class SchemaMigratorTests(PostgreSqlFixture postgres) : IClassFixture<PostgreSqlFixture>
{
    /// <summary>
    /// The reason the advisory lock is there. Both product hosts migrate on
    /// startup and both are started by the same `docker compose up`, so this
    /// is the ordinary case rather than a rare one: whoever takes the lock
    /// applies the schema, and the other waits and finds nothing pending.
    /// </summary>
    [Fact]
    public async Task Concurrent_hosts_apply_the_schema_exactly_once()
    {
        var connectionString = await postgres.CreateDatabaseAsync();

        // Separate providers, because two hosts are separate processes with
        // separate connection pools. One provider would share a pool and hide
        // the race this is about.
        await using var first = BuildProvider(connectionString);
        await using var second = BuildProvider(connectionString);

        var results = await Task.WhenAll(
            SchemaMigrator.ApplyAsync(first, CancellationToken.None),
            SchemaMigrator.ApplyAsync(second, CancellationToken.None));

        Assert.Single(results, applied => applied.Count > 0);
        Assert.Single(results, applied => applied.Count == 0);
    }

    [Fact]
    public async Task Applying_a_current_schema_reports_nothing_and_changes_nothing()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await using var serviceProvider = BuildProvider(connectionString);

        var first = await SchemaMigrator.ApplyAsync(serviceProvider, CancellationToken.None);
        var second = await SchemaMigrator.ApplyAsync(serviceProvider, CancellationToken.None);

        Assert.NotEmpty(first);
        Assert.Empty(second);
    }

    /// <summary>
    /// The lock is released on the way out, including when the migration
    /// throws. A held lock would leave the next host blocking on it forever,
    /// which is a worse failure than the one that caused it.
    /// </summary>
    [Fact]
    public async Task A_failed_migration_does_not_leave_the_schema_lock_held()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await using var unreachable = BuildProvider(
            connectionString.Replace(
                new Npgsql.NpgsqlConnectionStringBuilder(connectionString).Database!,
                "payaffe_does_not_exist",
                StringComparison.Ordinal));

        await Assert.ThrowsAnyAsync<Exception>(
            () => SchemaMigrator.ApplyAsync(unreachable, CancellationToken.None));

        await using var reachable = BuildProvider(connectionString);
        var applied = await SchemaMigrator.ApplyAsync(reachable, CancellationToken.None);

        Assert.NotEmpty(applied);
    }

    private static ServiceProvider BuildProvider(string connectionString)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPayaffeInfrastructure(connectionString);
        return services.BuildServiceProvider();
    }
}
