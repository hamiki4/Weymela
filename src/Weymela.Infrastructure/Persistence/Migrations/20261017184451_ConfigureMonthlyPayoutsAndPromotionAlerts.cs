using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Weymela.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ConfigureMonthlyPayoutsAndPromotionAlerts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Payout_Amounts",
                schema: "v3",
                table: "PayoutRecords");

            migrationBuilder.AddColumn<decimal>(
                name: "LowPromotionBalanceAlert",
                schema: "v3",
                table: "FinancialConfigurationVersions",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MonthlyPayoutDay",
                schema: "v3",
                table: "FinancialConfigurationVersions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "MonthlyPayoutEffectiveFromUtc",
                schema: "v3",
                table: "FinancialConfigurationVersions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "MonthlyPayoutEnabled",
                schema: "v3",
                table: "FinancialConfigurationVersions",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "UgcPayoutMinimumExempt",
                schema: "v3",
                table: "FinancialConfigurationVersions",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Payout_Amounts",
                schema: "v3",
                table: "PayoutRecords",
                sql: "\"Amount\" > 0 AND \"ThresholdUsed\" > 0 AND ((\"Beneficiary\"='Creator' AND \"CreatorId\" IS NOT NULL AND \"CustomerId\" IS NULL) OR (\"Beneficiary\"='Customer' AND \"CustomerId\" IS NOT NULL AND \"CreatorId\" IS NULL))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Payout_Amounts",
                schema: "v3",
                table: "PayoutRecords");

            migrationBuilder.DropColumn(
                name: "LowPromotionBalanceAlert",
                schema: "v3",
                table: "FinancialConfigurationVersions");

            migrationBuilder.DropColumn(
                name: "MonthlyPayoutDay",
                schema: "v3",
                table: "FinancialConfigurationVersions");

            migrationBuilder.DropColumn(
                name: "MonthlyPayoutEffectiveFromUtc",
                schema: "v3",
                table: "FinancialConfigurationVersions");

            migrationBuilder.DropColumn(
                name: "MonthlyPayoutEnabled",
                schema: "v3",
                table: "FinancialConfigurationVersions");

            migrationBuilder.DropColumn(
                name: "UgcPayoutMinimumExempt",
                schema: "v3",
                table: "FinancialConfigurationVersions");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Payout_Amounts",
                schema: "v3",
                table: "PayoutRecords",
                sql: "\"Amount\" >= \"ThresholdUsed\" AND \"ThresholdUsed\" > 0 AND ((\"Beneficiary\"='Creator' AND \"CreatorId\" IS NOT NULL AND \"CustomerId\" IS NULL) OR (\"Beneficiary\"='Customer' AND \"CustomerId\" IS NOT NULL AND \"CreatorId\" IS NULL))");
        }
    }
}
