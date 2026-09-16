using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Weymela.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPasswordCredentials : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "RecoveryGrantConsumedAtUtc",
                schema: "v3",
                table: "EmailAuthChallenges",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RecoveryGrantExpiresAtUtc",
                schema: "v3",
                table: "EmailAuthChallenges",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RecoveryGrantHash",
                schema: "v3",
                table: "EmailAuthChallenges",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PasswordCredentials",
                schema: "v3",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    PasswordHash = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Algorithm = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    HashVersion = table.Column<int>(type: "integer", nullable: false),
                    WorkFactor = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ChangedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    FailedAttempts = table.Column<int>(type: "integer", nullable: false),
                    LockedUntilUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PasswordCredentials", x => x.UserId);
                    table.CheckConstraint("CK_PasswordCredential_FailedAttempts", "\"FailedAttempts\" >= 0 AND \"FailedAttempts\" <= 10");
                    table.CheckConstraint("CK_PasswordCredential_Hash", "\"HashVersion\" > 0 AND \"WorkFactor\" >= 100000");
                });

            migrationBuilder.CreateIndex(
                name: "IX_EmailAuthChallenges_RecoveryGrantHash",
                schema: "v3",
                table: "EmailAuthChallenges",
                column: "RecoveryGrantHash",
                unique: true,
                filter: "\"RecoveryGrantHash\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PasswordCredentials_LockedUntilUtc",
                schema: "v3",
                table: "PasswordCredentials",
                column: "LockedUntilUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PasswordCredentials",
                schema: "v3");

            migrationBuilder.DropIndex(
                name: "IX_EmailAuthChallenges_RecoveryGrantHash",
                schema: "v3",
                table: "EmailAuthChallenges");

            migrationBuilder.DropColumn(
                name: "RecoveryGrantConsumedAtUtc",
                schema: "v3",
                table: "EmailAuthChallenges");

            migrationBuilder.DropColumn(
                name: "RecoveryGrantExpiresAtUtc",
                schema: "v3",
                table: "EmailAuthChallenges");

            migrationBuilder.DropColumn(
                name: "RecoveryGrantHash",
                schema: "v3",
                table: "EmailAuthChallenges");
        }
    }
}
