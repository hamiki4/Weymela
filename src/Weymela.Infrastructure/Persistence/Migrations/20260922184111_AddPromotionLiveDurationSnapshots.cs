using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Weymela.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPromotionLiveDurationSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PromotionLiveDurationDays",
                schema: "v3",
                table: "Promotions",
                type: "integer",
                nullable: false,
                defaultValue: 30);

            migrationBuilder.AddColumn<int>(
                name: "PromotionLiveDurationDays",
                schema: "v3",
                table: "FinancialConfigurationVersions",
                type: "integer",
                nullable: false,
                defaultValue: 30);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Promotion_LiveDuration",
                schema: "v3",
                table: "Promotions",
                sql: "\"PromotionLiveDurationDays\" BETWEEN 1 AND 365");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Configuration_PromotionLiveDuration",
                schema: "v3",
                table: "FinancialConfigurationVersions",
                sql: "\"PromotionLiveDurationDays\" BETWEEN 1 AND 365");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Promotion_LiveDuration",
                schema: "v3",
                table: "Promotions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Configuration_PromotionLiveDuration",
                schema: "v3",
                table: "FinancialConfigurationVersions");

            migrationBuilder.DropColumn(
                name: "PromotionLiveDurationDays",
                schema: "v3",
                table: "Promotions");

            migrationBuilder.DropColumn(
                name: "PromotionLiveDurationDays",
                schema: "v3",
                table: "FinancialConfigurationVersions");
        }
    }
}
