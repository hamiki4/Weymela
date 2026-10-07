using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Weymela.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AlignFinancialUatFlows : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_UgcCustomerOfferSale_Amounts",
                schema: "v3",
                table: "UgcCustomerOfferSales");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Payout_Amounts",
                schema: "v3",
                table: "PayoutRecords");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Cashback_PositiveSale",
                schema: "v3",
                table: "CustomerCashbackEntries");

            migrationBuilder.AddColumn<string>(
                name: "BenefitMode",
                schema: "v3",
                table: "UgcCustomerOfferSales",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "LegacyDiscount");

            migrationBuilder.AddColumn<string>(
                name: "BenefitMode",
                schema: "v3",
                table: "UgcCustomerOffers",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "LegacyDiscount");

            migrationBuilder.AddColumn<string>(
                name: "DestinationLegalName",
                schema: "v3",
                table: "PayoutRecords",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DestinationMethod",
                schema: "v3",
                table: "PayoutRecords",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DestinationProvider",
                schema: "v3",
                table: "PayoutRecords",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProtectedDestinationAccount",
                schema: "v3",
                table: "PayoutRecords",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DestinationAccountSnapshot",
                schema: "v3",
                table: "DepositRequests",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DestinationNameSnapshot",
                schema: "v3",
                table: "DepositRequests",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReceivingDestinationId",
                schema: "v3",
                table: "DepositRequests",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "UgcCustomerOfferSaleId",
                schema: "v3",
                table: "CustomerCashbackEntries",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PayoutDestinations",
                schema: "v3",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Beneficiary = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SubjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    Method = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Provider = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ProtectedAccount = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    AccountLast4 = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false),
                    LegalName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayoutDestinations", x => x.Id);
                    table.CheckConstraint("CK_PayoutDestination_Subject", "\"SubjectId\" <> '00000000-0000-0000-0000-000000000000' AND char_length(btrim(\"Provider\")) > 0 AND char_length(\"AccountLast4\") BETWEEN 2 AND 4 AND char_length(btrim(\"LegalName\")) > 0");
                });

            migrationBuilder.CreateTable(
                name: "PlatformReceivingDestinations",
                schema: "v3",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Method = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    AccountReference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlatformReceivingDestinations", x => x.Id);
                    table.CheckConstraint("CK_ReceivingDestination_Values", "char_length(btrim(\"Name\")) > 0 AND char_length(btrim(\"AccountReference\")) > 0 AND \"SortOrder\" >= 0");
                });

            migrationBuilder.InsertData(
                schema: "v3",
                table: "PlatformReceivingDestinations",
                columns: new[] { "Id", "AccountReference", "IsActive", "Method", "Name", "SortOrder", "UpdatedAtUtc", "UpdatedBy", "Version" },
                values: new object[,]
                {
                    { new Guid("10000000-0000-0000-0000-000000000001"), "0911111111", true, "Telebirr", "Telebirr", 1, new DateTime(2026, 10, 7, 0, 0, 0, 0, DateTimeKind.Utc), null, 0L },
                    { new Guid("10000000-0000-0000-0000-000000000002"), "1000000000", true, "Bank", "CBE", 2, new DateTime(2026, 10, 7, 0, 0, 0, 0, DateTimeKind.Utc), null, 0L },
                    { new Guid("10000000-0000-0000-0000-000000000003"), "123456789", true, "Bank", "Bank of Abyssinia", 3, new DateTime(2026, 10, 7, 0, 0, 0, 0, DateTimeKind.Utc), null, 0L }
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_UgcCustomerOfferSale_Amounts",
                schema: "v3",
                table: "UgcCustomerOfferSales",
                sql: "\"PurchaseAmount\" > 0 AND \"CustomerDiscountAmount\" > 0 AND \"CustomerPaysAmount\" >= 0 AND \"PlatformRevenueAmount\" >= 0 AND ((\"BenefitMode\"='LegacyDiscount' AND \"CustomerPaysAmount\" + \"CustomerDiscountAmount\" = \"PurchaseAmount\") OR (\"BenefitMode\"='Cashback' AND \"CustomerPaysAmount\" = \"PurchaseAmount\")) AND \"TotalOfferCharge\" = \"CustomerDiscountAmount\" + \"PlatformRevenueAmount\"");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Payout_Amounts",
                schema: "v3",
                table: "PayoutRecords",
                sql: "\"Amount\" >= \"ThresholdUsed\" AND \"ThresholdUsed\" > 0 AND ((\"Beneficiary\"='Creator' AND \"CreatorId\" IS NOT NULL AND \"CustomerId\" IS NULL) OR (\"Beneficiary\"='Customer' AND \"CustomerId\" IS NOT NULL AND \"CreatorId\" IS NULL))");

            migrationBuilder.CreateIndex(
                name: "IX_DepositRequests_ReceivingDestinationId",
                schema: "v3",
                table: "DepositRequests",
                column: "ReceivingDestinationId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerCashbackEntries_UgcCustomerOfferSaleId",
                schema: "v3",
                table: "CustomerCashbackEntries",
                column: "UgcCustomerOfferSaleId",
                unique: true,
                filter: "\"UgcCustomerOfferSaleId\" IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Cashback_PositiveSale",
                schema: "v3",
                table: "CustomerCashbackEntries",
                sql: "\"Amount\" > 0 AND ((\"Source\"='VerifiedSale' AND \"VerifiedSaleId\" IS NOT NULL AND \"UgcCustomerOfferSaleId\" IS NULL) OR (\"Source\"='UgcCustomerOfferSale' AND \"UgcCustomerOfferSaleId\" IS NOT NULL AND \"VerifiedSaleId\" IS NULL) OR (\"Source\"='AuthorizedAdjustment' AND \"VerifiedSaleId\" IS NULL AND \"UgcCustomerOfferSaleId\" IS NULL))");

            migrationBuilder.CreateIndex(
                name: "IX_PayoutDestinations_Beneficiary_SubjectId",
                schema: "v3",
                table: "PayoutDestinations",
                columns: new[] { "Beneficiary", "SubjectId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlatformReceivingDestinations_Name",
                schema: "v3",
                table: "PlatformReceivingDestinations",
                column: "Name",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_CustomerCashbackEntries_UgcCustomerOfferSales_UgcCustomerOf~",
                schema: "v3",
                table: "CustomerCashbackEntries",
                column: "UgcCustomerOfferSaleId",
                principalSchema: "v3",
                principalTable: "UgcCustomerOfferSales",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION v3.guard_cashback_sale() RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE journal_source text; journal_credit numeric;
                BEGIN
                  IF NEW."Source"='VerifiedSale' AND NOT EXISTS (
                    SELECT 1 FROM v3."VerifiedSales" WHERE "Id"=NEW."VerifiedSaleId"
                      AND "CustomerId"=NEW."CustomerId" AND "CustomerCashbackAmount"=NEW."Amount") THEN
                    RAISE EXCEPTION 'Cashback must match the eligible sale customer and amount' USING ERRCODE='23514';
                  END IF;
                  IF NEW."Source"='UgcCustomerOfferSale' AND NOT EXISTS (
                    SELECT 1 FROM v3."UgcCustomerOfferSales" WHERE "Id"=NEW."UgcCustomerOfferSaleId"
                      AND "CustomerId"=NEW."CustomerId" AND "CustomerDiscountAmount"=NEW."Amount"
                      AND "BenefitMode"='Cashback') THEN
                    RAISE EXCEPTION 'Cashback must match the eligible UGC sale customer and amount' USING ERRCODE='23514';
                  END IF;
                  IF NEW."Source" IN ('VerifiedSale','UgcCustomerOfferSale') THEN
                    SELECT j."SourceType", coalesce(sum(l."Amount") FILTER (WHERE l."Type"='Credit' AND l."Account"='CustomerCashbackPayable'),0)
                      INTO journal_source,journal_credit FROM v3."FinancialJournals" j
                      JOIN v3."FinancialJournalLines" l ON l."JournalId"=j."Id" WHERE j."Id"=NEW."JournalId" GROUP BY j."SourceType";
                    IF journal_credit<>NEW."Amount" OR
                       (NEW."Source"='VerifiedSale' AND journal_source<>'VerifiedSale') OR
                       (NEW."Source"='UgcCustomerOfferSale' AND journal_source<>'UgcCustomerOfferSale') THEN
                      RAISE EXCEPTION 'Cashback must reconcile with its authoritative journal' USING ERRCODE='23514';
                    END IF;
                  END IF;
                  RETURN NEW;
                END $$;

                CREATE FUNCTION v3.guard_customer_benefit_mode() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  IF NEW."BenefitMode"<>OLD."BenefitMode" THEN
                    RAISE EXCEPTION 'Customer benefit mode is immutable' USING ERRCODE='23514';
                  END IF;
                  RETURN NEW;
                END $$;
                CREATE TRIGGER customer_benefit_mode BEFORE UPDATE ON v3."UgcCustomerOffers"
                  FOR EACH ROW EXECUTE FUNCTION v3.guard_customer_benefit_mode();

                CREATE OR REPLACE FUNCTION v3.guard_payout_transition() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  IF OLD."Status" <> 'Eligible' OR NEW."Status" <> 'Paid' OR NEW."Amount" <> OLD."Amount" OR
                     NEW."ThresholdUsed" <> OLD."ThresholdUsed" OR NEW."ConfigurationVersionId" <> OLD."ConfigurationVersionId" OR
                     NEW."EligibleAtUtc" <> OLD."EligibleAtUtc" OR NEW."Beneficiary" <> OLD."Beneficiary" OR
                     NEW."CreatorId" IS DISTINCT FROM OLD."CreatorId" OR NEW."CustomerId" IS DISTINCT FROM OLD."CustomerId" OR
                     NEW."DestinationMethod" IS DISTINCT FROM OLD."DestinationMethod" OR
                     NEW."DestinationProvider" IS DISTINCT FROM OLD."DestinationProvider" OR
                     NEW."ProtectedDestinationAccount" IS DISTINCT FROM OLD."ProtectedDestinationAccount" OR
                     NEW."DestinationLegalName" IS DISTINCT FROM OLD."DestinationLegalName" OR
                     NEW."PaidAtUtc" IS NULL OR NEW."PaidBy" IS NULL OR NEW."JournalId" IS NULL OR
                     NEW."Reference" IS NULL OR length(trim(NEW."Reference"))=0 THEN
                    RAISE EXCEPTION 'Payout can only transition once to confirmed paid without changing its terms' USING ERRCODE='23514';
                  END IF;
                  RETURN NEW;
                END $$;
                """);

            migrationBuilder.AddForeignKey(
                name: "FK_DepositRequests_PlatformReceivingDestinations_ReceivingDest~",
                schema: "v3",
                table: "DepositRequests",
                column: "ReceivingDestinationId",
                principalSchema: "v3",
                principalTable: "PlatformReceivingDestinations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CustomerCashbackEntries_UgcCustomerOfferSales_UgcCustomerOf~",
                schema: "v3",
                table: "CustomerCashbackEntries");

            migrationBuilder.DropForeignKey(
                name: "FK_DepositRequests_PlatformReceivingDestinations_ReceivingDest~",
                schema: "v3",
                table: "DepositRequests");

            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS customer_benefit_mode ON v3."UgcCustomerOffers";
                DROP FUNCTION IF EXISTS v3.guard_customer_benefit_mode();
                CREATE OR REPLACE FUNCTION v3.guard_cashback_sale() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  IF NEW."Source"='VerifiedSale' AND NOT EXISTS (
                    SELECT 1 FROM v3."VerifiedSales" WHERE "Id"=NEW."VerifiedSaleId"
                      AND "CustomerId"=NEW."CustomerId" AND "CustomerCashbackAmount"=NEW."Amount") THEN
                    RAISE EXCEPTION 'Cashback must match the eligible sale customer and amount' USING ERRCODE='23514';
                  END IF;
                  RETURN NEW;
                END $$;
                CREATE OR REPLACE FUNCTION v3.guard_payout_transition() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  IF OLD."Status" <> 'Eligible' OR NEW."Status" <> 'Paid' OR NEW."Amount" <> OLD."Amount" OR
                     NEW."ThresholdUsed" <> OLD."ThresholdUsed" OR NEW."ConfigurationVersionId" <> OLD."ConfigurationVersionId" OR
                     NEW."EligibleAtUtc" <> OLD."EligibleAtUtc" OR NEW."Beneficiary" <> OLD."Beneficiary" OR
                     NEW."CreatorId" IS DISTINCT FROM OLD."CreatorId" OR NEW."CustomerId" IS DISTINCT FROM OLD."CustomerId" OR
                     NEW."PaidAtUtc" IS NULL OR NEW."PaidBy" IS NULL OR NEW."JournalId" IS NULL OR
                     NEW."Reference" IS NULL OR length(trim(NEW."Reference"))=0 THEN
                    RAISE EXCEPTION 'Payout can only transition once to confirmed paid without changing its terms' USING ERRCODE='23514';
                  END IF;
                  RETURN NEW;
                END $$;
                """);

            migrationBuilder.DropTable(
                name: "PayoutDestinations",
                schema: "v3");

            migrationBuilder.DropTable(
                name: "PlatformReceivingDestinations",
                schema: "v3");

            migrationBuilder.DropCheckConstraint(
                name: "CK_UgcCustomerOfferSale_Amounts",
                schema: "v3",
                table: "UgcCustomerOfferSales");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Payout_Amounts",
                schema: "v3",
                table: "PayoutRecords");

            migrationBuilder.DropIndex(
                name: "IX_DepositRequests_ReceivingDestinationId",
                schema: "v3",
                table: "DepositRequests");

            migrationBuilder.DropIndex(
                name: "IX_CustomerCashbackEntries_UgcCustomerOfferSaleId",
                schema: "v3",
                table: "CustomerCashbackEntries");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Cashback_PositiveSale",
                schema: "v3",
                table: "CustomerCashbackEntries");

            migrationBuilder.DropColumn(
                name: "BenefitMode",
                schema: "v3",
                table: "UgcCustomerOfferSales");

            migrationBuilder.DropColumn(
                name: "BenefitMode",
                schema: "v3",
                table: "UgcCustomerOffers");

            migrationBuilder.DropColumn(
                name: "DestinationLegalName",
                schema: "v3",
                table: "PayoutRecords");

            migrationBuilder.DropColumn(
                name: "DestinationMethod",
                schema: "v3",
                table: "PayoutRecords");

            migrationBuilder.DropColumn(
                name: "DestinationProvider",
                schema: "v3",
                table: "PayoutRecords");

            migrationBuilder.DropColumn(
                name: "ProtectedDestinationAccount",
                schema: "v3",
                table: "PayoutRecords");

            migrationBuilder.DropColumn(
                name: "DestinationAccountSnapshot",
                schema: "v3",
                table: "DepositRequests");

            migrationBuilder.DropColumn(
                name: "DestinationNameSnapshot",
                schema: "v3",
                table: "DepositRequests");

            migrationBuilder.DropColumn(
                name: "ReceivingDestinationId",
                schema: "v3",
                table: "DepositRequests");

            migrationBuilder.DropColumn(
                name: "UgcCustomerOfferSaleId",
                schema: "v3",
                table: "CustomerCashbackEntries");

            migrationBuilder.AddCheckConstraint(
                name: "CK_UgcCustomerOfferSale_Amounts",
                schema: "v3",
                table: "UgcCustomerOfferSales",
                sql: "\"PurchaseAmount\" > 0 AND \"CustomerDiscountAmount\" > 0 AND \"CustomerPaysAmount\" >= 0 AND \"PlatformRevenueAmount\" >= 0 AND \"CustomerPaysAmount\" + \"CustomerDiscountAmount\" = \"PurchaseAmount\" AND \"TotalOfferCharge\" = \"CustomerDiscountAmount\" + \"PlatformRevenueAmount\"");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Payout_Amounts",
                schema: "v3",
                table: "PayoutRecords",
                sql: "\"Amount\" > 0 AND \"Amount\" = \"ThresholdUsed\" AND ((\"Beneficiary\"='Creator' AND \"CreatorId\" IS NOT NULL AND \"CustomerId\" IS NULL) OR (\"Beneficiary\"='Customer' AND \"CustomerId\" IS NOT NULL AND \"CreatorId\" IS NULL))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Cashback_PositiveSale",
                schema: "v3",
                table: "CustomerCashbackEntries",
                sql: "\"Amount\" > 0 AND (\"Source\" <> 'VerifiedSale' OR \"VerifiedSaleId\" IS NOT NULL)");
        }
    }
}
