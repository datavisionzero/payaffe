using System.Net;
using Payaffe.Infrastructure;
using Payaffe.Infrastructure.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql;

namespace Payaffe.Integration.Tests.Persistence;

public sealed class ApiStartupMigrationTests(PostgreSqlFixture postgres) : IClassFixture<PostgreSqlFixture>
{
    [Fact]
    public async Task Api_startup_applies_database_migrations()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await using var factory = CreateFactory(connectionString);

        using var client = factory.CreateClient();
        await WaitForReadyAsync(client);

        Assert.NotEqual(0, await CountProductTablesAsync(connectionString));
    }

    /// <summary>
    /// The schema gate, not the connectivity one. The database here is
    /// reachable and empty, so a readiness endpoint that only asked whether it
    /// could connect would answer `ready` for an installation that cannot
    /// serve a single request (ADR 0027).
    /// </summary>
    [Fact]
    public async Task Api_readiness_is_not_ready_while_the_schema_has_not_been_applied()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await using var factory = CreateFactory(connectionString, applySchemaOnStartup: false);

        using var client = factory.CreateClient();
        var response = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("""{"status":"not_ready"}""", await response.Content.ReadAsStringAsync());
        Assert.Equal(0, await CountProductTablesAsync(connectionString));
    }

    [Fact]
    public async Task Api_startup_is_unaffected_by_a_schema_that_is_already_current()
    {
        var connectionString = await postgres.CreateDatabaseAsync();

        var services = new ServiceCollection();
        services.AddPayaffeInfrastructure(connectionString);
        await using (var serviceProvider = services.BuildServiceProvider())
        {
            await SchemaMigrator.ApplyAsync(serviceProvider, CancellationToken.None);
        }

        await using var factory = CreateFactory(connectionString);

        using var client = factory.CreateClient();
        await WaitForReadyAsync(client);
    }

    /// <summary>
    /// The migration does not block the host from starting (ADR 0027), so
    /// readiness is reached rather than observed on the first call. The wait
    /// is the test's, not the product's: a deployment waits the same way.
    /// </summary>
    private static async Task WaitForReadyAsync(HttpClient client)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(60);
        while (true)
        {
            var response = await client.GetAsync("/health/ready");
            var body = await response.Content.ReadAsStringAsync();
            if (response.StatusCode == HttpStatusCode.OK)
            {
                Assert.Equal("""{"status":"ready"}""", body);
                return;
            }

            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            if (DateTimeOffset.UtcNow > deadline)
            {
                Assert.Fail($"The API never became ready. Last response: {response.StatusCode} {body}");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }
    }

    private static async Task<long> CountProductTablesAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            select count(*)
            from information_schema.tables
            where table_schema in ('app', 'auth')
            """;

        return (long)(await command.ExecuteScalarAsync())!;
    }

    private static WebApplicationFactory<Program> CreateFactory(
        string connectionString,
        bool applySchemaOnStartup = true)
    {
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Testing");
                builder.ConfigureAppConfiguration((_, configuration) =>
                {
                    configuration.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["ConnectionStrings:Payaffe"] = connectionString,
                    });
                });
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<DbContextOptions<PayaffeDbContext>>();
                    services.RemoveAll<IDbContextOptionsConfiguration<PayaffeDbContext>>();
                    services.AddDbContext<PayaffeDbContext>(options => options.UseNpgsql(connectionString));

                    if (!applySchemaOnStartup)
                    {
                        // Only this one. Removing every IHostedService would
                        // take the web host itself out with it.
                        services.Remove(services.Single(descriptor =>
                            descriptor.ImplementationType == typeof(SchemaMigrationHostedService)));
                    }
                });
            });
    }
}
