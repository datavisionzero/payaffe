using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Payaffe.Infrastructure.Persistence;

#nullable disable

namespace Payaffe.Infrastructure.Migrations;

/// <summary>
/// Project ownership replaced the <c>(status, next_attempt_at)</c> index with a
/// Project-led one, which the delivery claim cannot use: it looks for due
/// events across every Project. Webhook events are never pruned, so without
/// this every poll scans the whole table. Partial, because delivered and
/// terminal events are the bulk of it and are never claimed.
/// </summary>
[DbContext(typeof(PayaffeDbContext))]
[Migration("202609240020_AddWebhookEventClaimIndex")]
public partial class AddWebhookEventClaimIndex : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            CREATE INDEX ix_webhook_events_due
                ON outbox.webhook_events (next_attempt_at, created_at)
                WHERE status in ('pending', 'retry_pending');
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DROP INDEX outbox.ix_webhook_events_due;");
    }
}
