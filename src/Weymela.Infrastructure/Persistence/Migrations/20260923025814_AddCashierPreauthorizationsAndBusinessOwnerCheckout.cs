using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Weymela.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCashierPreauthorizationsAndBusinessOwnerCheckout : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CashierPreauthorizations",
                schema: "v3",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uuid", nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    CanonicalPhone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    PhoneIdentifierHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ActivationCodeHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ActivationCodeExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ActivatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    UserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ActivationAttemptCount = table.Column<int>(type: "integer", nullable: false),
                    DisabledAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CashierPreauthorizations", x => x.Id);
                    table.CheckConstraint("CK_CashierPreauthorization_Activation", "(\"Status\" IN ('PendingActivation','Disabled') AND \"ActivatedAtUtc\" IS NULL AND \"UserId\" IS NULL) OR (\"Status\" IN ('Active','Disabled','Revoked') AND \"ActivatedAtUtc\" IS NOT NULL AND \"UserId\" IS NOT NULL)");
                    table.CheckConstraint("CK_CashierPreauthorization_Attempts", "\"ActivationAttemptCount\" >= 0 AND \"ActivationAttemptCount\" <= 5");
                    table.ForeignKey(
                        name: "FK_CashierPreauthorizations_BusinessWallets_BusinessId",
                        column: x => x.BusinessId,
                        principalSchema: "v3",
                        principalTable: "BusinessWallets",
                        principalColumn: "BusinessId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CashierPreauthorizations_BusinessId_CanonicalPhone_Status",
                schema: "v3",
                table: "CashierPreauthorizations",
                columns: new[] { "BusinessId", "CanonicalPhone", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_CashierPreauthorizations_PhoneIdentifierHash_Status",
                schema: "v3",
                table: "CashierPreauthorizations",
                columns: new[] { "PhoneIdentifierHash", "Status" });

            // Only active, approved Business owner permissions with a matching
            // Business public profile and wallet are deterministic backfill
            // candidates. Cashier and inactive/suspended permissions are not
            // changed.
            migrationBuilder.Sql("""
                UPDATE v3."CommercePermissions" AS permission
                SET "CanCheckout" = TRUE
                WHERE permission."Role" = 'Business'
                  AND permission."BusinessId" IS NOT NULL
                  AND permission."SubjectId" = permission."BusinessId"
                  AND permission."IsActive" = TRUE
                  AND permission."CanCheckout" = FALSE
                  AND EXISTS (
                      SELECT 1
                      FROM v3."PublicWorkspaceProfiles" AS profile
                      WHERE profile."SubjectId" = permission."SubjectId"
                        AND profile."Role" = 'Business'
                  )
                  AND EXISTS (
                      SELECT 1
                      FROM v3."BusinessWallets" AS wallet
                      WHERE wallet."BusinessId" = permission."BusinessId"
                  );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CashierPreauthorizations",
                schema: "v3");
        }
    }
}
