using Payaffe.Application;
using Payaffe.Infrastructure;
using Payaffe.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Payaffe.Integration.Tests.Persistence;

/// <summary>
/// The delivery claim looks for due events across every Project, so it needs an
/// index that does not lead with the Project.
/// </summary>
public sealed class WebhookClaimIndexMigrationTests(PostgreSqlFixture postgres) : IClassFixture<PostgreSqlFixture>
{
    [Fact]
    public async Task The_delivery_claim_can_use_a_partial_index_on_due_events()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPayaffeApplication();
        services.AddPayaffeInfrastructure(connectionString, registerHostedWorkers: false);
        await using var provider = services.BuildServiceProvider();
        await SchemaMigrator.ApplyAsync(provider, CancellationToken.None);

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using (var definition = new NpgsqlCommand(
            "select indexdef from pg_indexes where schemaname = 'outbox' and indexname = 'ix_webhook_events_due'",
            connection))
        {
            var indexDefinition = Assert.IsType<string>(await definition.ExecuteScalarAsync());
            Assert.Contains("(next_attempt_at, created_at)", indexDefinition, StringComparison.Ordinal);
            Assert.Contains("WHERE", indexDefinition, StringComparison.Ordinal);
            Assert.Contains("pending", indexDefinition, StringComparison.Ordinal);
            Assert.Contains("retry_pending", indexDefinition, StringComparison.Ordinal);
        }

        // An empty table would be scanned whichever indexes exist, so the
        // planner is told to avoid a sequential scan and asked what it would
        // use for the claim's shape instead.
        await using var transaction = await connection.BeginTransactionAsync();
        await using (var disableSeqScan = new NpgsqlCommand("set local enable_seqscan = off", connection, transaction))
        {
            await disableSeqScan.ExecuteNonQueryAsync();
        }

        await using var explain = new NpgsqlCommand(
            """
            explain select * from outbox.webhook_events
            where status in ('pending', 'retry_pending')
              and next_attempt_at <= now()
              and (locked_until is null or locked_until <= now())
            order by next_attempt_at, created_at
            limit 1
            """,
            connection,
            transaction);
        var plan = new List<string>();
        await using (var reader = await explain.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                plan.Add(reader.GetString(0));
            }
        }

        Assert.Contains(plan, line => line.Contains("ix_webhook_events_due", StringComparison.Ordinal));
    }
}
