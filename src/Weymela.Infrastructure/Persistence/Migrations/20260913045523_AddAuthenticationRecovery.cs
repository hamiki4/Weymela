using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Weymela.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAuthenticationRecovery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AuthIdentifiers",
                schema: "v3",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    IdentifierHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    IsVerified = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuthIdentifiers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AuthorizedDevices",
                schema: "v3",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CredentialKind = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    CredentialIdHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    EnrolledAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastUsedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RevokedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FailedAttempts = table.Column<int>(type: "integer", nullable: false),
                    LockedUntilUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuthorizedDevices", x => x.Id);
                    table.CheckConstraint("CK_AuthorizedDevice_FailedAttempts", "\"FailedAttempts\" >= 0");
                });

            migrationBuilder.CreateTable(
                name: "EmailAuthChallenges",
                schema: "v3",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    IdentifierHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Purpose = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CodeHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastSentAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ConsumedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    MaxAttempts = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailAuthChallenges", x => x.Id);
                    table.CheckConstraint("CK_EmailAuthChallenge_Attempts", "\"AttemptCount\" >= 0 AND \"AttemptCount\" <= \"MaxAttempts\"");
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuthIdentifiers_Kind_IdentifierHash",
                schema: "v3",
                table: "AuthIdentifiers",
                columns: new[] { "Kind", "IdentifierHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AuthIdentifiers_UserId",
                schema: "v3",
                table: "AuthIdentifiers",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AuthorizedDevices_UserId_CredentialIdHash",
                schema: "v3",
                table: "AuthorizedDevices",
                columns: new[] { "UserId", "CredentialIdHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AuthorizedDevices_UserId_RevokedAtUtc",
                schema: "v3",
                table: "AuthorizedDevices",
                columns: new[] { "UserId", "RevokedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_EmailAuthChallenges_ExpiresAtUtc_ConsumedAtUtc",
                schema: "v3",
                table: "EmailAuthChallenges",
                columns: new[] { "ExpiresAtUtc", "ConsumedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_EmailAuthChallenges_IdentifierHash_Purpose_CreatedAtUtc",
                schema: "v3",
                table: "EmailAuthChallenges",
                columns: new[] { "IdentifierHash", "Purpose", "CreatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuthIdentifiers",
                schema: "v3");

            migrationBuilder.DropTable(
                name: "AuthorizedDevices",
                schema: "v3");

            migrationBuilder.DropTable(
                name: "EmailAuthChallenges",
                schema: "v3");
        }
    }
}
