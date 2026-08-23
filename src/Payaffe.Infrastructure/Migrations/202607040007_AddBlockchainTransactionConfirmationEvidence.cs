using System;
using Payaffe.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Payaffe.Infrastructure.Migrations;

[DbContext(typeof(PayaffeDbContext))]
[Migration("202607040007_AddBlockchainTransactionConfirmationEvidence")]
public partial class AddBlockchainTransactionConfirmationEvidence : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "block_hash",
            schema: "app",
            table: "matching_blockchain_transactions",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<long>(
            name: "block_height",
            schema: "app",
            table: "matching_blockchain_transactions",
            type: "bigint",
            nullable: true);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "last_checked_at",
            schema: "app",
            table: "matching_blockchain_transactions",
            type: "timestamp with time zone",
            nullable: false,
            defaultValueSql: "'1970-01-01 00:00:00+00'::timestamp with time zone");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "block_hash",
            schema: "app",
            table: "matching_blockchain_transactions");

        migrationBuilder.DropColumn(
            name: "block_height",
            schema: "app",
            table: "matching_blockchain_transactions");

        migrationBuilder.DropColumn(
            name: "last_checked_at",
            schema: "app",
            table: "matching_blockchain_transactions");
    }
}
