using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Weymela.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddUgcCustomerOffers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "UgcCustomerOfferId",
                schema: "v3",
                table: "WalletEntries",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "PricingSnapshot_CustomerOfferPlatformSalePercent",
                schema: "v3",
                table: "UgcOpportunities",
                type: "numeric(9,4)",
                precision: 9,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "UgcCustomerOfferSaleId",
                schema: "v3",
                table: "PlatformRevenueEntries",
                type: "uuid",
                nullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "PromotionId",
                schema: "v3",
                table: "OfferQrSessions",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<Guid>(
                name: "CreatorId",
                schema: "v3",
                table: "OfferQrSessions",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<Guid>(
                name: "CreatorAllocationId",
                schema: "v3",
                table: "OfferQrSessions",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<string>(
                name: "Source",
                schema: "v3",
                table: "OfferQrSessions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "ViewAndSalePromotion");

            migrationBuilder.AddColumn<Guid>(
                name: "UgcCustomerOfferId",
                schema: "v3",
                table: "OfferQrSessions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "UgcCustomerOfferSaleId",
                schema: "v3",
                table: "OfferQrSessions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "UgcCustomerOfferId",
                schema: "v3",
                table: "FinancialJournals",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "UgcCustomerOfferSaleId",
                schema: "v3",
                table: "FinancialJournals",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Ugc_CustomerOfferPlatformSalePercent",
                schema: "v3",
                table: "FinancialConfigurationVersions",
                type: "numeric(9,4)",
                precision: 9,
                scale: 4,
                nullable: true);

            // Preserve Versions 1 and 2 exactly. Version 3 is the first snapshot
            // that governs UGC Customer Offer Sales and carries the approved 3% fee.
            migrationBuilder.Sql("""
                DO $$
                DECLARE configuration_count integer; version_two_count integer; version_three_count integer;
                BEGIN
                  SELECT count(*) INTO configuration_count FROM v3."FinancialConfigurations" WHERE "Name"='PlatformPricing';
                  SELECT count(*) INTO version_two_count FROM v3."FinancialConfigurationVersions" v
                    JOIN v3."FinancialConfigurations" c ON c."Id"=v."ConfigurationId"
                    WHERE c."Name"='PlatformPricing' AND v."Version"=2;
                  SELECT count(*) INTO version_three_count FROM v3."FinancialConfigurationVersions" v
                    JOIN v3."FinancialConfigurations" c ON c."Id"=v."ConfigurationId"
                    WHERE c."Name"='PlatformPricing' AND v."Version"=3;
                  IF configuration_count=0 AND version_two_count=0 AND version_three_count=0 THEN RETURN; END IF;
                  IF configuration_count<>1 OR version_two_count<>1 OR version_three_count<>0 THEN
                    RAISE EXCEPTION 'UGC Customer Offer migration requires the governed PlatformPricing Version 2 baseline';
                  END IF;
                END $$;

                WITH baseline AS (
                  SELECT v.* FROM v3."FinancialConfigurationVersions" v
                  JOIN v3."FinancialConfigurations" c ON c."Id"=v."ConfigurationId"
                  WHERE c."Name"='PlatformPricing' AND v."Version"=2
                ), next_version AS (
                  SELECT (substr(md5(v."Id"::text||':ugc-customer-offer-version-3'),1,8)||'-'||
                    substr(md5(v."Id"::text||':ugc-customer-offer-version-3'),9,4)||'-'||
                    substr(md5(v."Id"::text||':ugc-customer-offer-version-3'),13,4)||'-'||
                    substr(md5(v."Id"::text||':ugc-customer-offer-version-3'),17,4)||'-'||
                    substr(md5(v."Id"::text||':ugc-customer-offer-version-3'),21,12))::uuid AS "NewId",
                    transaction_timestamp() AS "NewEffectiveFromUtc", v.* FROM baseline v
                )
                INSERT INTO v3."FinancialConfigurationVersions" (
                  "Id","ConfigurationId","Version","ChangedBy","EffectiveFromUtc",
                  "ViewOnly_PromotionType","ViewOnly_ViewsPerReward","ViewOnly_BusinessCharge","ViewOnly_CreatorEarning","ViewOnly_PlatformEarning",
                  "ViewOnly_CreatorCommissionPercent","ViewOnly_CustomerCashbackPercent","ViewOnly_PlatformPercent","ViewOnly_EffectiveFromUtc","ViewOnly_ConfigurationVersionId","ViewOnly_MinimumPromotionBudget",
                  "ViewPlusCommission_PromotionType","ViewPlusCommission_ViewsPerReward","ViewPlusCommission_BusinessCharge","ViewPlusCommission_CreatorEarning","ViewPlusCommission_PlatformEarning",
                  "ViewPlusCommission_CreatorCommissionPercent","ViewPlusCommission_CustomerCashbackPercent","ViewPlusCommission_PlatformPercent","ViewPlusCommission_EffectiveFromUtc","ViewPlusCommission_ConfigurationVersionId","ViewPlusCommission_MinimumPromotionBudget",
                  "CreatorPayoutThreshold","CustomerPayoutThreshold","Ugc_ConfigurationVersionId","Ugc_EffectiveFromUtc","Ugc_MinimumCreatorPayment","Ugc_MinimumUgcBudget","Ugc_PlatformFeePercent","Ugc_CustomerOfferPlatformSalePercent")
                SELECT "NewId","ConfigurationId",3,"ChangedBy","NewEffectiveFromUtc",
                  "ViewOnly_PromotionType","ViewOnly_ViewsPerReward","ViewOnly_BusinessCharge","ViewOnly_CreatorEarning","ViewOnly_PlatformEarning",
                  "ViewOnly_CreatorCommissionPercent","ViewOnly_CustomerCashbackPercent","ViewOnly_PlatformPercent","NewEffectiveFromUtc","NewId","ViewOnly_MinimumPromotionBudget",
                  "ViewPlusCommission_PromotionType","ViewPlusCommission_ViewsPerReward","ViewPlusCommission_BusinessCharge","ViewPlusCommission_CreatorEarning","ViewPlusCommission_PlatformEarning",
                  "ViewPlusCommission_CreatorCommissionPercent","ViewPlusCommission_CustomerCashbackPercent","ViewPlusCommission_PlatformPercent","NewEffectiveFromUtc","NewId","ViewPlusCommission_MinimumPromotionBudget",
                  "CreatorPayoutThreshold","CustomerPayoutThreshold","NewId","NewEffectiveFromUtc","Ugc_MinimumCreatorPayment","Ugc_MinimumUgcBudget","Ugc_PlatformFeePercent",3.0000
                FROM next_version;
                """);

            migrationBuilder.AddColumn<Guid>(
                name: "UgcCustomerOfferId",
                schema: "v3",
                table: "AuditEvents",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "UgcCustomerOffers",
                schema: "v3",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UgcOpportunityId = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerFacingSlogan = table.Column<string>(type: "text", nullable: true),
                    CustomerDiscountPercent = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: false),
                    StartsAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndsAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    FundedLimit = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ReservedFunding = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    UsedFunding = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    PricingSnapshot_PlatformSalePercent = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: false),
                    PricingSnapshot_EffectiveFromUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PricingSnapshot_ConfigurationVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PublishedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UgcCustomerOffers", x => x.Id);
                    table.CheckConstraint("CK_UgcCustomerOffer_Dates", "\"EndsAtUtc\" > \"StartsAtUtc\"");
                    table.CheckConstraint("CK_UgcCustomerOffer_Funding", "\"FundedLimit\" > 0 AND \"ReservedFunding\" >= 0 AND \"UsedFunding\" >= 0 AND \"ReservedFunding\" + \"UsedFunding\" <= \"FundedLimit\"");
                    table.CheckConstraint("CK_UgcCustomerOffer_Percent", "\"CustomerDiscountPercent\" > 0 AND \"CustomerDiscountPercent\" <= 100");
                    table.ForeignKey(
                        name: "FK_UgcCustomerOffers_BusinessWallets_BusinessId",
                        column: x => x.BusinessId,
                        principalSchema: "v3",
                        principalTable: "BusinessWallets",
                        principalColumn: "BusinessId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UgcCustomerOffers_UgcOpportunities_UgcOpportunityId",
                        column: x => x.UgcOpportunityId,
                        principalSchema: "v3",
                        principalTable: "UgcOpportunities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "UgcCustomerOfferReservations",
                schema: "v3",
                columns: table => new
                {
                    UgcCustomerOfferId = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uuid", nullable: false),
                    OriginalAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    JournalId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UgcCustomerOfferReservations", x => x.UgcCustomerOfferId);
                    table.CheckConstraint("CK_UgcCustomerOfferReservation_Positive", "\"OriginalAmount\" > 0");
                    table.ForeignKey(
                        name: "FK_UgcCustomerOfferReservations_BusinessWallets_BusinessId",
                        column: x => x.BusinessId,
                        principalSchema: "v3",
                        principalTable: "BusinessWallets",
                        principalColumn: "BusinessId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UgcCustomerOfferReservations_FinancialJournals_JournalId",
                        column: x => x.JournalId,
                        principalSchema: "v3",
                        principalTable: "FinancialJournals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UgcCustomerOfferReservations_UgcCustomerOffers_UgcCustomerO~",
                        column: x => x.UgcCustomerOfferId,
                        principalSchema: "v3",
                        principalTable: "UgcCustomerOffers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "UgcCustomerOfferSales",
                schema: "v3",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UgcCustomerOfferId = table.Column<Guid>(type: "uuid", nullable: false),
                    UgcOpportunityId = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    CashierId = table.Column<Guid>(type: "uuid", nullable: false),
                    PurchaseAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    CustomerDiscountAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    CustomerPaysAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    PlatformRevenueAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    TotalOfferCharge = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    QrTokenReference = table.Column<string>(type: "text", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "text", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    JournalId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UgcCustomerOfferSales", x => x.Id);
                    table.CheckConstraint("CK_UgcCustomerOfferSale_Amounts", "\"PurchaseAmount\" > 0 AND \"CustomerDiscountAmount\" > 0 AND \"CustomerPaysAmount\" >= 0 AND \"PlatformRevenueAmount\" >= 0 AND \"CustomerPaysAmount\" + \"CustomerDiscountAmount\" = \"PurchaseAmount\" AND \"TotalOfferCharge\" = \"CustomerDiscountAmount\" + \"PlatformRevenueAmount\"");
                    table.ForeignKey(
                        name: "FK_UgcCustomerOfferSales_FinancialJournals_JournalId",
                        column: x => x.JournalId,
                        principalSchema: "v3",
                        principalTable: "FinancialJournals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UgcCustomerOfferSales_UgcCustomerOffers_UgcCustomerOfferId",
                        column: x => x.UgcCustomerOfferId,
                        principalSchema: "v3",
                        principalTable: "UgcCustomerOffers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UgcCustomerOfferSales_UgcOpportunities_UgcOpportunityId",
                        column: x => x.UgcOpportunityId,
                        principalSchema: "v3",
                        principalTable: "UgcOpportunities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "UgcCustomerOfferBudgetEntries",
                schema: "v3",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UgcCustomerOfferId = table.Column<Guid>(type: "uuid", nullable: false),
                    SaleId = table.Column<Guid>(type: "uuid", nullable: true),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Movement = table.Column<string>(type: "text", nullable: false),
                    JournalId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UgcCustomerOfferBudgetEntries", x => x.Id);
                    table.CheckConstraint("CK_UgcCustomerOfferBudgetEntry_Positive", "\"Amount\" > 0");
                    table.ForeignKey(
                        name: "FK_UgcCustomerOfferBudgetEntries_FinancialJournals_JournalId",
                        column: x => x.JournalId,
                        principalSchema: "v3",
                        principalTable: "FinancialJournals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UgcCustomerOfferBudgetEntries_UgcCustomerOfferSales_SaleId",
                        column: x => x.SaleId,
                        principalSchema: "v3",
                        principalTable: "UgcCustomerOfferSales",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UgcCustomerOfferBudgetEntries_UgcCustomerOffers_UgcCustomer~",
                        column: x => x.UgcCustomerOfferId,
                        principalSchema: "v3",
                        principalTable: "UgcCustomerOffers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PlatformRevenueEntries_UgcCustomerOfferSaleId",
                schema: "v3",
                table: "PlatformRevenueEntries",
                column: "UgcCustomerOfferSaleId");

            migrationBuilder.CreateIndex(
                name: "IX_OfferQrSessions_UgcCustomerOfferId",
                schema: "v3",
                table: "OfferQrSessions",
                column: "UgcCustomerOfferId");

            migrationBuilder.CreateIndex(
                name: "IX_OfferQrSessions_UgcCustomerOfferSaleId",
                schema: "v3",
                table: "OfferQrSessions",
                column: "UgcCustomerOfferSaleId",
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Qr_SaleBinding",
                schema: "v3",
                table: "OfferQrSessions",
                sql: "(\"SaleId\" IS NULL OR \"UgcCustomerOfferSaleId\" IS NULL) AND (\"Status\" <> 'Used' OR ((\"Source\" = 'ViewAndSalePromotion' AND \"SaleId\" IS NOT NULL AND \"UgcCustomerOfferSaleId\" IS NULL) OR (\"Source\" = 'UgcCustomerOffer' AND \"SaleId\" IS NULL AND \"UgcCustomerOfferSaleId\" IS NOT NULL)))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Qr_SourceBinding",
                schema: "v3",
                table: "OfferQrSessions",
                sql: "(\"Source\" = 'ViewAndSalePromotion' AND \"PromotionId\" IS NOT NULL AND \"CreatorId\" IS NOT NULL AND \"CreatorAllocationId\" IS NOT NULL AND \"UgcCustomerOfferId\" IS NULL) OR (\"Source\" = 'UgcCustomerOffer' AND \"PromotionId\" IS NULL AND \"CreatorId\" IS NULL AND \"CreatorAllocationId\" IS NULL AND \"UgcCustomerOfferId\" IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "IX_FinancialJournals_UgcCustomerOfferId",
                schema: "v3",
                table: "FinancialJournals",
                column: "UgcCustomerOfferId");

            migrationBuilder.CreateIndex(
                name: "IX_FinancialJournals_UgcCustomerOfferSaleId",
                schema: "v3",
                table: "FinancialJournals",
                column: "UgcCustomerOfferSaleId");

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_UgcCustomerOfferId_OccurredAtUtc",
                schema: "v3",
                table: "AuditEvents",
                columns: new[] { "UgcCustomerOfferId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_UgcCustomerOfferBudgetEntries_JournalId",
                schema: "v3",
                table: "UgcCustomerOfferBudgetEntries",
                column: "JournalId");

            migrationBuilder.CreateIndex(
                name: "IX_UgcCustomerOfferBudgetEntries_SaleId",
                schema: "v3",
                table: "UgcCustomerOfferBudgetEntries",
                column: "SaleId");

            migrationBuilder.CreateIndex(
                name: "IX_UgcCustomerOfferBudgetEntries_UgcCustomerOfferId_CreatedAtU~",
                schema: "v3",
                table: "UgcCustomerOfferBudgetEntries",
                columns: new[] { "UgcCustomerOfferId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_UgcCustomerOfferReservations_BusinessId",
                schema: "v3",
                table: "UgcCustomerOfferReservations",
                column: "BusinessId");

            migrationBuilder.CreateIndex(
                name: "IX_UgcCustomerOfferReservations_JournalId",
                schema: "v3",
                table: "UgcCustomerOfferReservations",
                column: "JournalId");

            migrationBuilder.CreateIndex(
                name: "IX_UgcCustomerOffers_BusinessId_Status",
                schema: "v3",
                table: "UgcCustomerOffers",
                columns: new[] { "BusinessId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_UgcCustomerOffers_UgcOpportunityId",
                schema: "v3",
                table: "UgcCustomerOffers",
                column: "UgcOpportunityId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UgcCustomerOfferSales_BusinessId_IdempotencyKey",
                schema: "v3",
                table: "UgcCustomerOfferSales",
                columns: new[] { "BusinessId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UgcCustomerOfferSales_JournalId",
                schema: "v3",
                table: "UgcCustomerOfferSales",
                column: "JournalId");

            migrationBuilder.CreateIndex(
                name: "IX_UgcCustomerOfferSales_UgcCustomerOfferId",
                schema: "v3",
                table: "UgcCustomerOfferSales",
                column: "UgcCustomerOfferId");

            migrationBuilder.CreateIndex(
                name: "IX_UgcCustomerOfferSales_UgcOpportunityId",
                schema: "v3",
                table: "UgcCustomerOfferSales",
                column: "UgcOpportunityId");

            migrationBuilder.AddForeignKey(
                name: "FK_OfferQrSessions_UgcCustomerOfferSales_UgcCustomerOfferSaleId",
                schema: "v3",
                table: "OfferQrSessions",
                column: "UgcCustomerOfferSaleId",
                principalSchema: "v3",
                principalTable: "UgcCustomerOfferSales",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_OfferQrSessions_UgcCustomerOffers_UgcCustomerOfferId",
                schema: "v3",
                table: "OfferQrSessions",
                column: "UgcCustomerOfferId",
                principalSchema: "v3",
                principalTable: "UgcCustomerOffers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PlatformRevenueEntries_UgcCustomerOfferSales_UgcCustomerOff~",
                schema: "v3",
                table: "PlatformRevenueEntries",
                column: "UgcCustomerOfferSaleId",
                principalSchema: "v3",
                principalTable: "UgcCustomerOfferSales",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.Sql(UgcCustomerOfferIntegritySql.Create);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_OfferQrSessions_UgcCustomerOfferSales_UgcCustomerOfferSaleId",
                schema: "v3",
                table: "OfferQrSessions");

            migrationBuilder.DropForeignKey(
                name: "FK_OfferQrSessions_UgcCustomerOffers_UgcCustomerOfferId",
                schema: "v3",
                table: "OfferQrSessions");

            migrationBuilder.DropForeignKey(
                name: "FK_PlatformRevenueEntries_UgcCustomerOfferSales_UgcCustomerOff~",
                schema: "v3",
                table: "PlatformRevenueEntries");

            migrationBuilder.DropTable(
                name: "UgcCustomerOfferBudgetEntries",
                schema: "v3");

            migrationBuilder.DropTable(
                name: "UgcCustomerOfferReservations",
                schema: "v3");

            migrationBuilder.DropTable(
                name: "UgcCustomerOfferSales",
                schema: "v3");

            migrationBuilder.DropTable(
                name: "UgcCustomerOffers",
                schema: "v3");

            migrationBuilder.DropIndex(
                name: "IX_PlatformRevenueEntries_UgcCustomerOfferSaleId",
                schema: "v3",
                table: "PlatformRevenueEntries");

            migrationBuilder.DropIndex(
                name: "IX_OfferQrSessions_UgcCustomerOfferId",
                schema: "v3",
                table: "OfferQrSessions");

            migrationBuilder.DropIndex(
                name: "IX_OfferQrSessions_UgcCustomerOfferSaleId",
                schema: "v3",
                table: "OfferQrSessions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Qr_SaleBinding",
                schema: "v3",
                table: "OfferQrSessions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Qr_SourceBinding",
                schema: "v3",
                table: "OfferQrSessions");

            migrationBuilder.DropIndex(
                name: "IX_FinancialJournals_UgcCustomerOfferId",
                schema: "v3",
                table: "FinancialJournals");

            migrationBuilder.DropIndex(
                name: "IX_FinancialJournals_UgcCustomerOfferSaleId",
                schema: "v3",
                table: "FinancialJournals");

            migrationBuilder.DropIndex(
                name: "IX_AuditEvents_UgcCustomerOfferId_OccurredAtUtc",
                schema: "v3",
                table: "AuditEvents");

            migrationBuilder.DropColumn(
                name: "UgcCustomerOfferId",
                schema: "v3",
                table: "WalletEntries");

            migrationBuilder.DropColumn(
                name: "PricingSnapshot_CustomerOfferPlatformSalePercent",
                schema: "v3",
                table: "UgcOpportunities");

            migrationBuilder.DropColumn(
                name: "UgcCustomerOfferSaleId",
                schema: "v3",
                table: "PlatformRevenueEntries");

            migrationBuilder.DropColumn(
                name: "Source",
                schema: "v3",
                table: "OfferQrSessions");

            migrationBuilder.DropColumn(
                name: "UgcCustomerOfferId",
                schema: "v3",
                table: "OfferQrSessions");

            migrationBuilder.DropColumn(
                name: "UgcCustomerOfferSaleId",
                schema: "v3",
                table: "OfferQrSessions");

            migrationBuilder.DropColumn(
                name: "UgcCustomerOfferId",
                schema: "v3",
                table: "FinancialJournals");

            migrationBuilder.DropColumn(
                name: "UgcCustomerOfferSaleId",
                schema: "v3",
                table: "FinancialJournals");

            migrationBuilder.DropColumn(
                name: "Ugc_CustomerOfferPlatformSalePercent",
                schema: "v3",
                table: "FinancialConfigurationVersions");

            migrationBuilder.DropColumn(
                name: "UgcCustomerOfferId",
                schema: "v3",
                table: "AuditEvents");

            migrationBuilder.AlterColumn<Guid>(
                name: "PromotionId",
                schema: "v3",
                table: "OfferQrSessions",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "CreatorId",
                schema: "v3",
                table: "OfferQrSessions",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "CreatorAllocationId",
                schema: "v3",
                table: "OfferQrSessions",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }
    }
}
