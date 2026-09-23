using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Payaffe.Infrastructure.Migrations;

[DbContext(typeof(Persistence.PayaffeDbContext))]
[Migration("202609240012_AddReorgSignalCounts")]
public sealed class AddReorgSignalCounts : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Reorg monitoring raises a Reorg Alert only for a signal that
        // persists across consecutive checks, so a provider that lags a block
        // or misses a transaction once is not taken for a reorganisation.
        migrationBuilder.Sql(
            """
            ALTER TABLE app.matching_blockchain_transactions
                ADD COLUMN consecutive_missing_count integer NOT NULL DEFAULT 0,
                ADD COLUMN consecutive_confirmation_drop_count integer NOT NULL DEFAULT 0;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(
            """
            ALTER TABLE app.matching_blockchain_transactions
                DROP COLUMN consecutive_missing_count,
                DROP COLUMN consecutive_confirmation_drop_count;
            """);
}
