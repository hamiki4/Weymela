using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Weymela.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerProfiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CustomerProfiles",
                schema: "v3",
                columns: table => new
                {
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    PreferredName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerProfiles", x => x.CustomerId);
                    table.CheckConstraint("CK_CustomerProfile_PreferredName", "char_length(btrim(\"PreferredName\")) BETWEEN 1 AND 120");
                });

            migrationBuilder.CreateIndex(
                name: "IX_CustomerProfiles_UserId",
                schema: "v3",
                table: "CustomerProfiles",
                column: "UserId",
                unique: true);

            // Existing Customer identities remain the same identities. This
            // additive backfill creates only the new typed private projection;
            // it does not rewrite public IDs, permissions or auth state.
            migrationBuilder.Sql("""
                INSERT INTO v3."CustomerProfiles"
                    ("CustomerId", "UserId", "PreferredName", "CreatedAtUtc", "UpdatedAtUtc", "Version")
                SELECT permission."SubjectId", permission."UserId",
                       COALESCE(NULLIF(btrim(profile."DisplayName"), ''), 'Customer'),
                       CURRENT_TIMESTAMP, CURRENT_TIMESTAMP, 0
                FROM v3."CommercePermissions" AS permission
                INNER JOIN v3."PublicWorkspaceProfiles" AS profile
                    ON profile."SubjectId" = permission."SubjectId"
                   AND profile."Role" = permission."Role"
                WHERE permission."Role" = 'Customer';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CustomerProfiles",
                schema: "v3");
        }
    }
}
