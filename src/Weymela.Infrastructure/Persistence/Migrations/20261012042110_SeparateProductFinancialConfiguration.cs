using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Weymela.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SeparateProductFinancialConfiguration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "ViewPlusCommission_ViewsPerReward",
                schema: "v3",
                table: "FinancialConfigurationVersions",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<string>(
                name: "ViewPlusCommission_PromotionType",
                schema: "v3",
                table: "FinancialConfigurationVersions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64);

            migrationBuilder.AlterColumn<decimal>(
                name: "ViewPlusCommission_PlatformPercent",
                schema: "v3",
                table: "FinancialConfigurationVersions",
                type: "numeric(9,4)",
                precision: 9,
                scale: 4,
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(9,4)",
                oldPrecision: 9,
                oldScale: 4);

            migrationBuilder.AlterColumn<decimal>(
                name: "ViewPlusCommission_PlatformEarning",
                schema: "v3",
                table: "FinancialConfigurationVersions",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,2)",
                oldPrecision: 18,
                oldScale: 2);

            migrationBuilder.AlterColumn<DateTime>(
                name: "ViewPlusCommission_EffectiveFromUtc",
                schema: "v3",
                table: "FinancialConfigurationVersions",
                type: "timestamp with time zone",
                nullable: true,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone");

            migrationBuilder.AlterColumn<decimal>(
                name: "ViewPlusCommission_CustomerCashbackPercent",
                schema: "v3",
                table: "FinancialConfigurationVersions",
                type: "numeric(9,4)",
                precision: 9,
                scale: 4,
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(9,4)",
                oldPrecision: 9,
                oldScale: 4);

            migrationBuilder.AlterColumn<decimal>(
                name: "ViewPlusCommission_CreatorEarning",
                schema: "v3",
                table: "FinancialConfigurationVersions",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,2)",
                oldPrecision: 18,
                oldScale: 2);

            migrationBuilder.AlterColumn<decimal>(
                name: "ViewPlusCommission_CreatorCommissionPercent",
                schema: "v3",
                table: "FinancialConfigurationVersions",
                type: "numeric(9,4)",
                precision: 9,
                scale: 4,
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(9,4)",
                oldPrecision: 9,
                oldScale: 4);

            migrationBuilder.AlterColumn<Guid>(
                name: "ViewPlusCommission_ConfigurationVersionId",
                schema: "v3",
                table: "FinancialConfigurationVersions",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<decimal>(
                name: "ViewPlusCommission_BusinessCharge",
                schema: "v3",
                table: "FinancialConfigurationVersions",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,2)",
                oldPrecision: 18,
                oldScale: 2);

            migrationBuilder.AlterColumn<int>(
                name: "ViewOnly_ViewsPerReward",
                schema: "v3",
                table: "FinancialConfigurationVersions",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<string>(
                name: "ViewOnly_PromotionType",
                schema: "v3",
                table: "FinancialConfigurationVersions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64);

            migrationBuilder.AlterColumn<decimal>(
                name: "ViewOnly_PlatformPercent",
                schema: "v3",
                table: "FinancialConfigurationVersions",
                type: "numeric(9,4)",
                precision: 9,
                scale: 4,
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(9,4)",
                oldPrecision: 9,
                oldScale: 4);

            migrationBuilder.AlterColumn<decimal>(
                name: "ViewOnly_PlatformEarning",
                schema: "v3",
                table: "FinancialConfigurationVersions",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,2)",
                oldPrecision: 18,
                oldScale: 2);

            migrationBuilder.AlterColumn<DateTime>(
                name: "ViewOnly_EffectiveFromUtc",
                schema: "v3",
                table: "FinancialConfigurationVersions",
                type: "timestamp with time zone",
                nullable: true,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone");

            migrationBuilder.AlterColumn<decimal>(
                name: "ViewOnly_CustomerCashbackPercent",
                schema: "v3",
                table: "FinancialConfigurationVersions",
                type: "numeric(9,4)",
                precision: 9,
                scale: 4,
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(9,4)",
                oldPrecision: 9,
                oldScale: 4);

            migrationBuilder.AlterColumn<decimal>(
                name: "ViewOnly_CreatorEarning",
                schema: "v3",
                table: "FinancialConfigurationVersions",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,2)",
                oldPrecision: 18,
                oldScale: 2);

            migrationBuilder.AlterColumn<decimal>(
                name: "ViewOnly_CreatorCommissionPercent",
                schema: "v3",
                table: "FinancialConfigurationVersions",
                type: "numeric(9,4)",
                precision: 9,
                scale: 4,
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(9,4)",
                oldPrecision: 9,
                oldScale: 4);

            migrationBuilder.AlterColumn<Guid>(
                name: "ViewOnly_ConfigurationVersionId",
                schema: "v3",
                table: "FinancialConfigurationVersions",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<decimal>(
                name: "ViewOnly_BusinessCharge",
                schema: "v3",
                table: "FinancialConfigurationVersions",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,2)",
                oldPrecision: 18,
                oldScale: 2);

            migrationBuilder.AlterColumn<int>(
                name: "PromotionLiveDurationDays",
                schema: "v3",
                table: "FinancialConfigurationVersions",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<bool>(
                name: "EnforceAudienceRequirements",
                schema: "v3",
                table: "FinancialConfigurationVersions",
                type: "boolean",
                nullable: true,
                oldClrType: typeof(bool),
                oldType: "boolean");

            migrationBuilder.AlterColumn<decimal>(
                name: "CustomerPayoutThreshold",
                schema: "v3",
                table: "FinancialConfigurationVersions",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,2)",
                oldPrecision: 18,
                oldScale: 2);

            migrationBuilder.AlterColumn<decimal>(
                name: "CreatorPayoutThreshold",
                schema: "v3",
                table: "FinancialConfigurationVersions",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,2)",
                oldPrecision: 18,
                oldScale: 2);

            migrationBuilder.AddColumn<decimal>(
                name: "ApprovedCreatorCommissionPercent",
                schema: "v3",
                table: "FinancialConfigurationVersions",
                type: "numeric(9,4)",
                precision: 9,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ApprovedCustomerCashbackPercent",
                schema: "v3",
                table: "FinancialConfigurationVersions",
                type: "numeric(9,4)",
                precision: 9,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ApprovedPlatformPercent",
                schema: "v3",
                table: "FinancialConfigurationVersions",
                type: "numeric(9,4)",
                precision: 9,
                scale: 4,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Configuration_ApprovedSaleRates",
                schema: "v3",
                table: "FinancialConfigurationVersions",
                sql: "(\"ApprovedCreatorCommissionPercent\" IS NOT NULL AND \"ApprovedCustomerCashbackPercent\" IS NOT NULL AND \"ApprovedPlatformPercent\" IS NOT NULL AND \"ApprovedCreatorCommissionPercent\" BETWEEN 0 AND 100 AND \"ApprovedCustomerCashbackPercent\" BETWEEN 0 AND 100 AND \"ApprovedPlatformPercent\" BETWEEN 0 AND 100 AND \"ApprovedCreatorCommissionPercent\"+\"ApprovedCustomerCashbackPercent\"+\"ApprovedPlatformPercent\"<=100) OR (\"ApprovedCreatorCommissionPercent\" IS NULL AND \"ApprovedCustomerCashbackPercent\" IS NULL AND \"ApprovedPlatformPercent\" IS NULL AND \"ViewPlusCommission_CreatorCommissionPercent\" IS NOT NULL AND \"ViewPlusCommission_CustomerCashbackPercent\" IS NOT NULL AND \"ViewPlusCommission_PlatformPercent\" IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Configuration_OptionalPayoutMinimums",
                schema: "v3",
                table: "FinancialConfigurationVersions",
                sql: "(\"CreatorPayoutThreshold\" IS NULL OR \"CreatorPayoutThreshold\">0) AND (\"CustomerPayoutThreshold\" IS NULL OR \"CustomerPayoutThreshold\">0)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                  IF EXISTS(SELECT 1 FROM v3."FinancialConfigurationVersions" WHERE "ViewOnly_PromotionType" IS NULL OR "ViewPlusCommission_PromotionType" IS NULL OR "CreatorPayoutThreshold" IS NULL OR "CustomerPayoutThreshold" IS NULL OR "PromotionLiveDurationDays" IS NULL OR "EnforceAudienceRequirements" IS NULL) THEN
                    RAISE EXCEPTION 'Cannot downgrade partial financial settings; preserve version history and roll back routing or fix forward.';
                  END IF;
                END $$;
                """);
            migrationBuilder.DropCheckConstraint(
                name: "CK_Configuration_ApprovedSaleRates",
                schema: "v3",
                table: "FinancialConfigurationVersions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Configuration_OptionalPayoutMinimums",
                schema: "v3",
                table: "FinancialConfigurationVersions");

            migrationBuilder.DropColumn(
                name: "ApprovedCreatorCommissionPercent",
                schema: "v3",
                table: "FinancialConfigurationVersions");

            migrationBuilder.DropColumn(
                name: "ApprovedCustomerCashbackPercent",
                schema: "v3",
                table: "FinancialConfigurationVersions");

            migrationBuilder.DropColumn(
                name: "ApprovedPlatformPercent",
                schema: "v3",
                table: "FinancialConfigurationVersions");

            migrationBuilder.AlterColumn<int>(
                name: "ViewPlusCommission_ViewsPerReward",
                schema: "v3",
                table: "FinancialConfigurationVersions",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ViewPlusCommission_PromotionType",
                schema: "v3",
                table: "FinancialConfigurationVersions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64,
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "ViewPlusCommission_PlatformPercent",
                schema: "v3",
                table: "FinancialConfigurationVersions",
                type: "numeric(9,4)",
                precision: 9,
                scale: 4,
                nullable: false,
                defaultValue: 0m,
                oldClrType: typeof(decimal),
                oldType: "numeric(9,4)",
                oldPrecision: 9,
                oldScale: 4,
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "ViewPlusCommission_PlatformEarning",
                schema: "v3",
                table: "FinancialConfigurationVersions",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,2)",
                oldPrecision: 18,
                oldScale: 2,
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTime>(
                name: "ViewPlusCommission_EffectiveFromUtc",
                schema: "v3",
                table: "FinancialConfigurationVersions",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified),
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "ViewPlusCommission_CustomerCashbackPercent",
                schema: "v3",
                table: "FinancialConfigurationVersions",
                type: "numeric(9,4)",
                precision: 9,
                scale: 4,
                nullable: false,
                defaultValue: 0m,
                oldClrType: typeof(decimal),
                oldType: "numeric(9,4)",
                oldPrecision: 9,
                oldScale: 4,
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "ViewPlusCommission_CreatorEarning",
                schema: "v3",
                table: "FinancialConfigurationVersions",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,2)",
                oldPrecision: 18,
                oldScale: 2,
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "ViewPlusCommission_CreatorCommissionPercent",
                schema: "v3",
                table: "FinancialConfigurationVersions",
                type: "numeric(9,4)",
                precision: 9,
                scale: 4,
                nullable: false,
                defaultValue: 0m,
                oldClrType: typeof(decimal),
                oldType: "numeric(9,4)",
                oldPrecision: 9,
                oldScale: 4,
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "ViewPlusCommission_ConfigurationVersionId",
                schema: "v3",
                table: "FinancialConfigurationVersions",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "ViewPlusCommission_BusinessCharge",
                schema: "v3",
                table: "FinancialConfigurationVersions",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,2)",
                oldPrecision: 18,
                oldScale: 2,
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "ViewOnly_ViewsPerReward",
                schema: "v3",
                table: "FinancialConfigurationVersions",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ViewOnly_PromotionType",
                schema: "v3",
                table: "FinancialConfigurationVersions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64,
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "ViewOnly_PlatformPercent",
                schema: "v3",
                table: "FinancialConfigurationVersions",
                type: "numeric(9,4)",
                precision: 9,
                scale: 4,
                nullable: false,
                defaultValue: 0m,
                oldClrType: typeof(decimal),
                oldType: "numeric(9,4)",
                oldPrecision: 9,
                oldScale: 4,
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "ViewOnly_PlatformEarning",
                schema: "v3",
                table: "FinancialConfigurationVersions",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,2)",
                oldPrecision: 18,
                oldScale: 2,
                oldNullable: true);

            migrationBuilder.AlterColumn<DateTime>(
                name: "ViewOnly_EffectiveFromUtc",
                schema: "v3",
                table: "FinancialConfigurationVersions",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified),
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "ViewOnly_CustomerCashbackPercent",
                schema: "v3",
                table: "FinancialConfigurationVersions",
                type: "numeric(9,4)",
                precision: 9,
                scale: 4,
                nullable: false,
                defaultValue: 0m,
                oldClrType: typeof(decimal),
                oldType: "numeric(9,4)",
                oldPrecision: 9,
                oldScale: 4,
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "ViewOnly_CreatorEarning",
                schema: "v3",
                table: "FinancialConfigurationVersions",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,2)",
                oldPrecision: 18,
                oldScale: 2,
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "ViewOnly_CreatorCommissionPercent",
                schema: "v3",
                table: "FinancialConfigurationVersions",
                type: "numeric(9,4)",
                precision: 9,
                scale: 4,
                nullable: false,
                defaultValue: 0m,
                oldClrType: typeof(decimal),
                oldType: "numeric(9,4)",
                oldPrecision: 9,
                oldScale: 4,
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "ViewOnly_ConfigurationVersionId",
                schema: "v3",
                table: "FinancialConfigurationVersions",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "ViewOnly_BusinessCharge",
                schema: "v3",
                table: "FinancialConfigurationVersions",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,2)",
                oldPrecision: 18,
                oldScale: 2,
                oldNullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "PromotionLiveDurationDays",
                schema: "v3",
                table: "FinancialConfigurationVersions",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AlterColumn<bool>(
                name: "EnforceAudienceRequirements",
                schema: "v3",
                table: "FinancialConfigurationVersions",
                type: "boolean",
                nullable: false,
                defaultValue: false,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "CustomerPayoutThreshold",
                schema: "v3",
                table: "FinancialConfigurationVersions",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,2)",
                oldPrecision: 18,
                oldScale: 2,
                oldNullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "CreatorPayoutThreshold",
                schema: "v3",
                table: "FinancialConfigurationVersions",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,2)",
                oldPrecision: 18,
                oldScale: 2,
                oldNullable: true);
        }
    }
}
