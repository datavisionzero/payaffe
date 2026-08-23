using Payaffe.Infrastructure;
using Payaffe.Infrastructure.Persistence;
using Payaffe.Migrations;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql;

namespace Payaffe.Integration.Tests.Persistence;

public sealed class ApiStartupMigrationTests(PostgreSqlFixture postgres) : IClassFixture<PostgreSqlFixture>
{
    [Fact]
    public async Task Api_startup_does_not_apply_database_migrations()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await using var factory = CreateFactory(connectionString);

        using var client = factory.CreateClient();
        var response = await client.GetAsync("/health/live");
        response.EnsureSuccessStatusCode();

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            select count(*)
            from information_schema.tables
            where table_schema in ('app', 'auth')
            """;

        var tableCount = (long)(await command.ExecuteScalarAsync())!;
        Assert.Equal(0, tableCount);
    }

    [Fact]
    public async Task Api_readiness_succeeds_after_controlled_migration_run()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await ApplyMigrationsAsync(connectionString);

        await using var factory = CreateFactory(connectionString);

        using var client = factory.CreateClient();
        var response = await client.GetAsync("/health/ready");

        response.EnsureSuccessStatusCode();
        Assert.Equal("""{"status":"ready"}""", await response.Content.ReadAsStringAsync());
    }

    private static async Task ApplyMigrationsAsync(string connectionString)
    {
        var services = new ServiceCollection();
        services.AddPayaffeInfrastructure(connectionString);

        using var serviceProvider = services.BuildServiceProvider();
        await MigrationRunner.ApplyAsync(serviceProvider, CancellationToken.None);
    }

    private static WebApplicationFactory<Program> CreateFactory(string connectionString)
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
                });
            });
    }
}
