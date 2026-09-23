using Payaffe.Application;
using Payaffe.Infrastructure;
using Payaffe.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Payaffe.Integration.Tests.Persistence;

/// <summary>
/// The migrations are hand-written SQL and there is no EF model snapshot, so
/// nothing else notices when <see cref="PayaffeDbContext"/> and the schema the
/// migrations build stop describing the same database. This compares the two
/// for every mapped table: columns, types and nullability, keys, foreign keys,
/// indexes and check constraints.
/// </summary>
public sealed class SchemaDriftTests(PostgreSqlFixture postgres) : IClassFixture<PostgreSqlFixture>
{
    private static readonly string[] ProductSchemas = ["app", "audit", "auth", "outbox"];

    // Tables the migrations create that the model deliberately does not map.
    private static readonly string[] UnmappedTables =
    [
        // Read and written with raw SQL under the schema lock (ADR 0033).
        "app.installation",
    ];

    [Fact]
    public async Task Migrated_schema_matches_the_ef_model()
    {
        var connectionString = await postgres.CreateDatabaseAsync();
        await using var provider = BuildProvider(connectionString);
        await SchemaMigrator.ApplyAsync(provider, CancellationToken.None);

        await using var scope = provider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PayaffeDbContext>();
        var model = dbContext.GetService<IDesignTimeModel>().Model.GetRelationalModel();
        var database = await DatabaseSchema.ReadAsync(connectionString);

        var expected = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var table in model.Tables)
        {
            var name = $"{table.Schema}.{table.Name}";
            expected.Add($"table {name}");
            foreach (var column in table.Columns)
            {
                expected.Add(
                    $"column {name}.{column.Name} {Normalize(column.StoreType)} " +
                    (column.IsNullable ? "null" : "not null"));
            }

            if (table.PrimaryKey is { } primaryKey)
            {
                expected.Add($"primary key {name}.{primaryKey.Name} ({Columns(primaryKey.Columns)})");
            }

            foreach (var uniqueConstraint in table.UniqueConstraints.Where(constraint => !constraint.GetIsPrimaryKey()))
            {
                expected.Add($"unique {name}.{uniqueConstraint.Name} ({Columns(uniqueConstraint.Columns)})");
            }

            foreach (var foreignKey in table.ForeignKeyConstraints)
            {
                expected.Add(
                    $"foreign key {name}.{foreignKey.Name} ({Columns(foreignKey.Columns)}) -> " +
                    $"{foreignKey.PrincipalTable.Schema}.{foreignKey.PrincipalTable.Name} " +
                    $"({Columns(foreignKey.PrincipalColumns)}) on delete {OnDelete(foreignKey.OnDeleteAction)}");
            }

            foreach (var index in table.Indexes)
            {
                expected.Add(
                    $"{(index.IsUnique ? "unique" : "index")} {name}.{index.Name} ({Columns(index.Columns)})");
            }

            foreach (var checkConstraint in table.CheckConstraints)
            {
                expected.Add($"check {name}.{checkConstraint.Name}");
            }
        }

        var actual = new SortedSet<string>(
            database.Where(fact => !UnmappedTables.Any(table => fact.Contains($" {table}.", StringComparison.Ordinal) ||
                                                              fact == $"table {table}")),
            StringComparer.Ordinal);

        var missingFromDatabase = expected.Except(actual).ToArray();
        var missingFromModel = actual.Except(expected).ToArray();
        Assert.True(
            missingFromDatabase.Length == 0 && missingFromModel.Length == 0,
            "The EF model and the migrated schema disagree." +
            Environment.NewLine + "In the model only:" + Environment.NewLine + "  " +
            string.Join(Environment.NewLine + "  ", missingFromDatabase) +
            Environment.NewLine + "In the database only:" + Environment.NewLine + "  " +
            string.Join(Environment.NewLine + "  ", missingFromModel));
    }

    private static string Columns(IEnumerable<IColumnBase> columns) =>
        string.Join(", ", columns.Select(column => column.Name));

    private static string OnDelete(ReferentialAction action) => action switch
    {
        ReferentialAction.Cascade => "cascade",
        ReferentialAction.Restrict => "restrict",
        ReferentialAction.SetNull => "set null",
        ReferentialAction.SetDefault => "set default",
        _ => "no action",
    };

    // PostgreSQL spells a few types differently from how the model declares
    // them; only the spelling is folded here, never the type.
    private static string Normalize(string storeType) => storeType.ToLowerInvariant() switch
    {
        "timestamptz" => "timestamp with time zone",
        "int" or "int4" => "integer",
        "int8" => "bigint",
        "int2" => "smallint",
        "bool" => "boolean",
        "varchar" => "character varying",
        var other => other,
    };

    private static ServiceProvider BuildProvider(string connectionString)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPayaffeApplication();
        services.AddPayaffeInfrastructure(connectionString, registerHostedWorkers: false);
        return services.BuildServiceProvider();
    }

    private static class DatabaseSchema
    {
        public static async Task<IReadOnlyList<string>> ReadAsync(string connectionString)
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            var facts = new List<string>();

            await ReadAsync(
                """
                select 'table ' || table_schema || '.' || table_name
                from information_schema.tables
                where table_schema = any(@schemas) and table_type = 'BASE TABLE'
                """);

            await ReadAsync(
                """
                select 'column ' || n.nspname || '.' || c.relname || '.' || a.attname || ' ' ||
                       format_type(a.atttypid, a.atttypmod) || ' ' ||
                       case when a.attnotnull then 'not null' else 'null' end
                from pg_attribute a
                join pg_class c on c.oid = a.attrelid
                join pg_namespace n on n.oid = c.relnamespace
                where n.nspname = any(@schemas) and c.relkind = 'r' and a.attnum > 0 and not a.attisdropped
                """);

            // Keys, foreign keys and check constraints, with their columns in
            // constraint order.
            await ReadAsync(
                """
                select case con.contype
                           when 'p' then 'primary key '
                           when 'u' then 'unique '
                       end || n.nspname || '.' || c.relname || '.' || con.conname || ' (' ||
                       (select string_agg(a.attname, ', ' order by k.ordinality)
                        from unnest(con.conkey) with ordinality k(attnum, ordinality)
                        join pg_attribute a on a.attrelid = con.conrelid and a.attnum = k.attnum) || ')'
                from pg_constraint con
                join pg_class c on c.oid = con.conrelid
                join pg_namespace n on n.oid = c.relnamespace
                where n.nspname = any(@schemas) and con.contype in ('p', 'u')
                union all
                select 'foreign key ' || n.nspname || '.' || c.relname || '.' || con.conname || ' (' ||
                       (select string_agg(a.attname, ', ' order by k.ordinality)
                        from unnest(con.conkey) with ordinality k(attnum, ordinality)
                        join pg_attribute a on a.attrelid = con.conrelid and a.attnum = k.attnum) || ') -> ' ||
                       pn.nspname || '.' || pc.relname || ' (' ||
                       (select string_agg(a.attname, ', ' order by k.ordinality)
                        from unnest(con.confkey) with ordinality k(attnum, ordinality)
                        join pg_attribute a on a.attrelid = con.confrelid and a.attnum = k.attnum) || ') on delete ' ||
                       case con.confdeltype
                           when 'c' then 'cascade'
                           when 'r' then 'restrict'
                           when 'n' then 'set null'
                           when 'd' then 'set default'
                           else 'no action'
                       end
                from pg_constraint con
                join pg_class c on c.oid = con.conrelid
                join pg_namespace n on n.oid = c.relnamespace
                join pg_class pc on pc.oid = con.confrelid
                join pg_namespace pn on pn.oid = pc.relnamespace
                where n.nspname = any(@schemas) and con.contype = 'f'
                union all
                select 'check ' || n.nspname || '.' || c.relname || '.' || con.conname
                from pg_constraint con
                join pg_class c on c.oid = con.conrelid
                join pg_namespace n on n.oid = c.relnamespace
                where n.nspname = any(@schemas) and con.contype = 'c'
                """);

            // Indexes that no constraint owns; a primary key or unique
            // constraint brings its own index and is compared above. A unique
            // index and a unique constraint are the same fact to the database,
            // so both read as `unique`, as the model's alternate keys and
            // unique indexes do.
            await ReadAsync(
                """
                select case when x.indisunique then 'unique ' else 'index ' end ||
                       n.nspname || '.' || t.relname || '.' || i.relname || ' (' ||
                       (select string_agg(a.attname, ', ' order by k.ordinality)
                        from unnest(x.indkey::smallint[]) with ordinality k(attnum, ordinality)
                        join pg_attribute a on a.attrelid = x.indrelid and a.attnum = k.attnum) || ')'
                from pg_index x
                join pg_class i on i.oid = x.indexrelid
                join pg_class t on t.oid = x.indrelid
                join pg_namespace n on n.oid = t.relnamespace
                where n.nspname = any(@schemas)
                  and not exists (select 1 from pg_constraint con where con.conindid = x.indexrelid and con.contype in ('p', 'u'))
                """);

            return facts;

            async Task ReadAsync(string sql)
            {
                await using var command = connection.CreateCommand();
                command.CommandText = sql;
                command.Parameters.AddWithValue("schemas", ProductSchemas);
                await using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    facts.Add(reader.GetString(0));
                }
            }
        }
    }
}
