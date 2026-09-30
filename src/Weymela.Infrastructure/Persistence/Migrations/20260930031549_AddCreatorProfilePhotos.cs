using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Weymela.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCreatorProfilePhotos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CreatorPhotoKey",
                schema: "v3",
                table: "PublicWorkspaceProfiles",
                type: "character varying(66)",
                maxLength: 66,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_PublicProfile_CreatorPhotoKey",
                schema: "v3",
                table: "PublicWorkspaceProfiles",
                sql: "\"CreatorPhotoKey\" IS NULL OR (\"Role\"='Creator' AND \"CreatorPhotoKey\" ~ '^p_[0-9a-f]{64}$')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DO $$ BEGIN IF EXISTS (SELECT 1 FROM v3.\"PublicWorkspaceProfiles\" WHERE \"CreatorPhotoKey\" IS NOT NULL) THEN RAISE EXCEPTION 'Restore from backup before dropping active Creator photo references'; END IF; END $$;");
            migrationBuilder.DropCheckConstraint(
                name: "CK_PublicProfile_CreatorPhotoKey",
                schema: "v3",
                table: "PublicWorkspaceProfiles");

            migrationBuilder.DropColumn(
                name: "CreatorPhotoKey",
                schema: "v3",
                table: "PublicWorkspaceProfiles");
        }
    }
}
