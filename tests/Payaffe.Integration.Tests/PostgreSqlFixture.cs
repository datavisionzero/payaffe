using Npgsql;
using Testcontainers.PostgreSql;

namespace Payaffe.Integration.Tests;

public sealed class PostgreSqlFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:17-alpine")
        .WithDatabase("payaffe_tests")
        .WithUsername("payaffe")
        .WithPassword("payaffe")
        .Build();

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
    }

    public async Task DisposeAsync()
    {
        await _container.DisposeAsync();
    }

    public async Task<string> CreateDatabaseAsync()
    {
        var databaseName = "payaffe_" + Guid.NewGuid().ToString("N");
        await using var connection = new NpgsqlConnection(GetAdminConnectionString());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"create database {QuoteIdentifier(databaseName)}";
        await command.ExecuteNonQueryAsync();

        var builder = new NpgsqlConnectionStringBuilder(_container.GetConnectionString())
        {
            Database = databaseName,
        };

        return builder.ToString();
    }

    public async Task<string> CloneDatabaseAsync(string sourceConnectionString)
    {
        var sourceDatabase = new NpgsqlConnectionStringBuilder(sourceConnectionString).Database
            ?? throw new ArgumentException("Source database is required.", nameof(sourceConnectionString));
        var databaseName = "payaffe_" + Guid.NewGuid().ToString("N");
        await using var connection = new NpgsqlConnection(GetAdminConnectionString());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            $"create database {QuoteIdentifier(databaseName)} template {QuoteIdentifier(sourceDatabase)}";
        await command.ExecuteNonQueryAsync();

        var builder = new NpgsqlConnectionStringBuilder(_container.GetConnectionString())
        {
            Database = databaseName,
        };

        return builder.ToString();
    }

    private string GetAdminConnectionString()
    {
        var builder = new NpgsqlConnectionStringBuilder(_container.GetConnectionString())
        {
            Database = "postgres",
        };

        return builder.ToString();
    }

    private static string QuoteIdentifier(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";
}
