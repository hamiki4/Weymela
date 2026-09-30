using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Weymela.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUgcPlatformCapacities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SelectedPlatform",
                schema: "v3",
                table: "UgcCreatorRequests",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "VerifiedSocialProfileId",
                schema: "v3",
                table: "UgcCreatorRequests",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "UgcPlatformCapacities",
                schema: "v3",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UgcOpportunityId = table.Column<Guid>(type: "uuid", nullable: false),
                    Platform = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Capacity = table.Column<int>(type: "integer", nullable: false),
                    ApprovedCount = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UgcPlatformCapacities", x => x.Id);
                    table.CheckConstraint("CK_UgcPlatformCapacity_Counts", "\"Capacity\" >= 0 AND \"ApprovedCount\" >= 0 AND \"ApprovedCount\" <= \"Capacity\"");
                    table.ForeignKey(
                        name: "FK_UgcPlatformCapacities_UgcOpportunities_UgcOpportunityId",
                        column: x => x.UgcOpportunityId,
                        principalSchema: "v3",
                        principalTable: "UgcOpportunities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UgcCreatorRequests_VerifiedSocialProfileId",
                schema: "v3",
                table: "UgcCreatorRequests",
                column: "VerifiedSocialProfileId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_UgcCreatorRequest_PlatformBinding",
                schema: "v3",
                table: "UgcCreatorRequests",
                sql: "(\"SelectedPlatform\" IS NULL) = (\"VerifiedSocialProfileId\" IS NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_UgcPlatformCapacities_UgcOpportunityId_Platform",
                schema: "v3",
                table: "UgcPlatformCapacities",
                columns: new[] { "UgcOpportunityId", "Platform" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_UgcCreatorRequests_CreatorSocialProfiles_VerifiedSocialProf~",
                schema: "v3",
                table: "UgcCreatorRequests",
                column: "VerifiedSocialProfileId",
                principalSchema: "v3",
                principalTable: "CreatorSocialProfiles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Attributed Creator activity must never be silently collapsed back
            // into the legacy global count. Use the reviewed paired restore
            // procedure once this feature has been used.
            migrationBuilder.Sql(@"DO $$ BEGIN
                IF EXISTS (SELECT 1 FROM v3.""UgcPlatformCapacities"")
                    OR EXISTS (SELECT 1 FROM v3.""UgcCreatorRequests"" WHERE ""SelectedPlatform"" IS NOT NULL)
                THEN RAISE EXCEPTION 'UGC platform capacity data exists; restore from a reviewed backup instead of downgrading';
                END IF;
            END $$;");
            migrationBuilder.DropForeignKey(
                name: "FK_UgcCreatorRequests_CreatorSocialProfiles_VerifiedSocialProf~",
                schema: "v3",
                table: "UgcCreatorRequests");

            migrationBuilder.DropTable(
                name: "UgcPlatformCapacities",
                schema: "v3");

            migrationBuilder.DropIndex(
                name: "IX_UgcCreatorRequests_VerifiedSocialProfileId",
                schema: "v3",
                table: "UgcCreatorRequests");

            migrationBuilder.DropCheckConstraint(
                name: "CK_UgcCreatorRequest_PlatformBinding",
                schema: "v3",
                table: "UgcCreatorRequests");

            migrationBuilder.DropColumn(
                name: "SelectedPlatform",
                schema: "v3",
                table: "UgcCreatorRequests");

            migrationBuilder.DropColumn(
                name: "VerifiedSocialProfileId",
                schema: "v3",
                table: "UgcCreatorRequests");
        }
    }
}
