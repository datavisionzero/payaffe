using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Payaffe.Infrastructure.Migrations;

[DbContext(typeof(Persistence.PayaffeDbContext))]
[Migration("202609240010_AddPaymentLastPolledAt")]
public sealed class AddPaymentLastPolledAt : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Blockchain Observation polls the least recently polled Payments
        // first, so a batch rotates through every active Payment instead of
        // polling the same oldest ones on every tick.
        migrationBuilder.Sql(
            """
            ALTER TABLE app.payments ADD COLUMN last_polled_at timestamp with time zone NULL;

            CREATE INDEX ix_payments_observation_rotation
                ON app.payments (last_polled_at, id)
                WHERE status in ('waiting_for_payment', 'observed');
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql(
            """
            DROP INDEX app.ix_payments_observation_rotation;
            ALTER TABLE app.payments DROP COLUMN last_polled_at;
            """);
}
