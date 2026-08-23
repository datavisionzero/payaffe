using Payaffe.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Payaffe.Infrastructure.Migrations;

/// <summary>
/// A session may now exist without a second factor ever having been cleared
/// (ADR 0028).
/// </summary>
/// <remarks>
/// Both columns were required because every session had passed TOTP by
/// construction. With TOTP optional they have to be able to say "never", and
/// the honest way to say it is null — writing the sign-in time into
/// `mfa_authenticated_at` would record in the security store that a factor was
/// cleared which never was.
/// </remarks>
[DbContext(typeof(PayaffeDbContext))]
[Migration("202608230001_AllowSessionsWithoutASecondFactor")]
public sealed partial class AllowSessionsWithoutASecondFactor : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<DateTimeOffset>(
            name: "mfa_authenticated_at",
            schema: "auth",
            table: "admin_sessions",
            type: "timestamp with time zone",
            nullable: true,
            oldClrType: typeof(DateTimeOffset),
            oldType: "timestamp with time zone",
            oldNullable: false);

        migrationBuilder.AlterColumn<DateTimeOffset>(
            name: "step_up_authenticated_at",
            schema: "auth",
            table: "admin_sessions",
            type: "timestamp with time zone",
            nullable: true,
            oldClrType: typeof(DateTimeOffset),
            oldType: "timestamp with time zone",
            oldNullable: false);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Down is not a supported operation for this product; it exists because
        // the tooling generates it. Sessions with a null timestamp cannot be
        // expressed by the old schema, so going back would have to invent one.
        throw new NotSupportedException(
            "Reverting this migration would have to invent a second-factor timestamp for sessions that never cleared one.");
    }
}
