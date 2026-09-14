using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Weymela.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDevicePinSessionFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_AuthorizedDevice_FailedAttempts",
                schema: "v3",
                table: "AuthorizedDevices");

            migrationBuilder.AddColumn<DateTime>(
                name: "ExpiresAtUtc",
                schema: "v3",
                table: "AuthorizedDevices",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PinVerifier",
                schema: "v3",
                table: "AuthorizedDevices",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "RequiresRecovery",
                schema: "v3",
                table: "AuthorizedDevices",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "DeviceSessions",
                schema: "v3",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    IdentityBindingId = table.Column<Guid>(type: "uuid", nullable: false),
                    IdentityVersion = table.Column<long>(type: "bigint", nullable: false),
                    AuthorizedDeviceId = table.Column<Guid>(type: "uuid", nullable: false),
                    SessionIdentifierHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastActivityAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LockedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RevokedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Generation = table.Column<long>(type: "bigint", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeviceSessions", x => x.Id);
                    table.CheckConstraint("CK_DeviceSession_Generation", "\"Generation\" > 0");
                    table.CheckConstraint("CK_DeviceSession_Lifetime", "\"ExpiresAtUtc\" > \"CreatedAtUtc\" AND \"ExpiresAtUtc\" <= \"CreatedAtUtc\" + INTERVAL '1 hour'");
                    table.ForeignKey(
                        name: "FK_DeviceSessions_AuthorizedDevices_AuthorizedDeviceId",
                        column: x => x.AuthorizedDeviceId,
                        principalSchema: "v3",
                        principalTable: "AuthorizedDevices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DeviceSessions_IdentityBindings_IdentityBindingId",
                        column: x => x.IdentityBindingId,
                        principalSchema: "v3",
                        principalTable: "IdentityBindings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_AuthorizedDevice_Cooldown",
                schema: "v3",
                table: "AuthorizedDevices",
                sql: "\"LockedUntilUtc\" IS NULL OR \"FailedAttempts\" >= 5");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AuthorizedDevice_Expiry",
                schema: "v3",
                table: "AuthorizedDevices",
                sql: "\"ExpiresAtUtc\" IS NULL OR \"ExpiresAtUtc\" = \"EnrolledAtUtc\" + INTERVAL '30 days'");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AuthorizedDevice_FailedAttempts",
                schema: "v3",
                table: "AuthorizedDevices",
                sql: "\"FailedAttempts\" >= 0 AND \"FailedAttempts\" <= 10");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AuthorizedDevice_Recovery",
                schema: "v3",
                table: "AuthorizedDevices",
                sql: "\"RequiresRecovery\" = (\"FailedAttempts\" = 10)");

            migrationBuilder.CreateIndex(
                name: "IX_DeviceSessions_AuthorizedDeviceId_CreatedAtUtc",
                schema: "v3",
                table: "DeviceSessions",
                columns: new[] { "AuthorizedDeviceId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_DeviceSessions_IdentityBindingId",
                schema: "v3",
                table: "DeviceSessions",
                column: "IdentityBindingId");

            migrationBuilder.CreateIndex(
                name: "IX_DeviceSessions_SessionIdentifierHash",
                schema: "v3",
                table: "DeviceSessions",
                column: "SessionIdentifierHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DeviceSessions_UserId_RevokedAtUtc",
                schema: "v3",
                table: "DeviceSessions",
                columns: new[] { "UserId", "RevokedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DeviceSessions",
                schema: "v3");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AuthorizedDevice_Cooldown",
                schema: "v3",
                table: "AuthorizedDevices");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AuthorizedDevice_Expiry",
                schema: "v3",
                table: "AuthorizedDevices");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AuthorizedDevice_FailedAttempts",
                schema: "v3",
                table: "AuthorizedDevices");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AuthorizedDevice_Recovery",
                schema: "v3",
                table: "AuthorizedDevices");

            migrationBuilder.DropColumn(
                name: "ExpiresAtUtc",
                schema: "v3",
                table: "AuthorizedDevices");

            migrationBuilder.DropColumn(
                name: "PinVerifier",
                schema: "v3",
                table: "AuthorizedDevices");

            migrationBuilder.DropColumn(
                name: "RequiresRecovery",
                schema: "v3",
                table: "AuthorizedDevices");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AuthorizedDevice_FailedAttempts",
                schema: "v3",
                table: "AuthorizedDevices",
                sql: "\"FailedAttempts\" >= 0");
        }
    }
}
