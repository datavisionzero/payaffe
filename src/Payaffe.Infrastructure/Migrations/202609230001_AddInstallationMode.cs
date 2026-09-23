using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Payaffe.Infrastructure.Persistence;

#nullable disable

namespace Payaffe.Infrastructure.Migrations;

[DbContext(typeof(PayaffeDbContext))]
[Migration("202609230001_AddInstallationMode")]
public partial class AddInstallationMode : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // A database that already holds Payments predates Test Mode, so those
        // Payments are real and the installation is live. An empty one is
        // recorded by the first host that starts, in the mode it is given.
        migrationBuilder.Sql(
            """
            CREATE TABLE app.installation (
                id smallint NOT NULL,
                mode text NOT NULL,
                recorded_at timestamp with time zone NOT NULL,
                CONSTRAINT pk_installation PRIMARY KEY (id),
                CONSTRAINT ck_installation_single_row CHECK (id = 1),
                CONSTRAINT ck_installation_mode CHECK (mode in ('live', 'test'))
            );

            INSERT INTO app.installation (id, mode, recorded_at)
            SELECT 1, 'live', now()
            WHERE EXISTS (SELECT 1 FROM app.payments);
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("DROP TABLE app.installation;");
}
