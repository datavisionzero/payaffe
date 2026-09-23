using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Payaffe.Infrastructure.Persistence;

#nullable disable

namespace Payaffe.Infrastructure.Migrations;

[DbContext(typeof(PayaffeDbContext))]
[Migration("202609240001_AddAdminSecondFactorLimits")]
public partial class AddAdminSecondFactorLimits : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            ALTER TABLE auth.admin_accounts ADD COLUMN last_totp_time_step bigint NULL;
            ALTER TABLE auth.admin_accounts
                ADD COLUMN failed_second_factor_attempt_count integer NOT NULL DEFAULT 0;
            ALTER TABLE auth.admin_accounts
                ADD COLUMN second_factor_locked_until timestamp with time zone NULL;
            ALTER TABLE auth.admin_accounts
                ADD CONSTRAINT ck_admin_accounts_failed_second_factor_attempt_count
                CHECK (failed_second_factor_attempt_count >= 0);
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            """
            ALTER TABLE auth.admin_accounts
                DROP CONSTRAINT ck_admin_accounts_failed_second_factor_attempt_count;
            ALTER TABLE auth.admin_accounts DROP COLUMN second_factor_locked_until;
            ALTER TABLE auth.admin_accounts DROP COLUMN failed_second_factor_attempt_count;
            ALTER TABLE auth.admin_accounts DROP COLUMN last_totp_time_step;
            """);
    }
}
