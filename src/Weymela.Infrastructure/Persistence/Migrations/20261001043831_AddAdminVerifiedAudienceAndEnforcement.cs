using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Weymela.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAdminVerifiedAudienceAndEnforcement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "MinimumAudience",
                schema: "v3",
                table: "PromotionPlatforms",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "EnforceAudienceRequirements",
                schema: "v3",
                table: "FinancialConfigurationVersions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "AudienceVerificationSource",
                schema: "v3",
                table: "CreatorSocialProfiles",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "LegacyUnknown");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PromotionPlatform_Audience",
                schema: "v3",
                table: "PromotionPlatforms",
                sql: "\"MinimumAudience\" IS NULL OR \"MinimumAudience\" >= 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_PromotionPlatform_Audience",
                schema: "v3",
                table: "PromotionPlatforms");

            migrationBuilder.DropColumn(
                name: "MinimumAudience",
                schema: "v3",
                table: "PromotionPlatforms");

            migrationBuilder.DropColumn(
                name: "EnforceAudienceRequirements",
                schema: "v3",
                table: "FinancialConfigurationVersions");

            migrationBuilder.DropColumn(
                name: "AudienceVerificationSource",
                schema: "v3",
                table: "CreatorSocialProfiles");
        }
    }
}
