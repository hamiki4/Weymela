using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Weymela.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBusinessProfileCoordinates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "Latitude",
                schema: "v3",
                table: "PublicWorkspaceProfiles",
                type: "numeric(9,6)",
                precision: 9,
                scale: 6,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Longitude",
                schema: "v3",
                table: "PublicWorkspaceProfiles",
                type: "numeric(9,6)",
                precision: 9,
                scale: 6,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_PublicProfile_CoordinatesPair",
                schema: "v3",
                table: "PublicWorkspaceProfiles",
                sql: "(\"Latitude\" IS NULL AND \"Longitude\" IS NULL) OR (\"Latitude\" IS NOT NULL AND \"Longitude\" IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PublicProfile_LatitudeRange",
                schema: "v3",
                table: "PublicWorkspaceProfiles",
                sql: "\"Latitude\" IS NULL OR \"Latitude\" BETWEEN -90 AND 90");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PublicProfile_LongitudeRange",
                schema: "v3",
                table: "PublicWorkspaceProfiles",
                sql: "\"Longitude\" IS NULL OR \"Longitude\" BETWEEN -180 AND 180");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_PublicProfile_CoordinatesPair",
                schema: "v3",
                table: "PublicWorkspaceProfiles");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PublicProfile_LatitudeRange",
                schema: "v3",
                table: "PublicWorkspaceProfiles");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PublicProfile_LongitudeRange",
                schema: "v3",
                table: "PublicWorkspaceProfiles");

            migrationBuilder.DropColumn(
                name: "Latitude",
                schema: "v3",
                table: "PublicWorkspaceProfiles");

            migrationBuilder.DropColumn(
                name: "Longitude",
                schema: "v3",
                table: "PublicWorkspaceProfiles");
        }
    }
}
