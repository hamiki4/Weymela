using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Weymela.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BindUgcSaleAssignments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Qr_SourceBinding",
                schema: "v3",
                table: "OfferQrSessions");

            migrationBuilder.AddColumn<Guid>(
                name: "UgcAssignmentId",
                schema: "v3",
                table: "UgcCustomerOfferSales",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "UgcAssignmentId",
                schema: "v3",
                table: "OfferQrSessions",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_UgcCustomerOfferSales_UgcAssignmentId",
                schema: "v3",
                table: "UgcCustomerOfferSales",
                column: "UgcAssignmentId");

            migrationBuilder.CreateIndex(
                name: "IX_OfferQrSessions_UgcAssignmentId",
                schema: "v3",
                table: "OfferQrSessions",
                column: "UgcAssignmentId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Qr_SourceBinding",
                schema: "v3",
                table: "OfferQrSessions",
                sql: "(\"Source\" = 'ViewAndSalePromotion' AND \"PromotionId\" IS NOT NULL AND \"CreatorId\" IS NOT NULL AND \"CreatorAllocationId\" IS NOT NULL AND \"UgcCustomerOfferId\" IS NULL AND \"UgcAssignmentId\" IS NULL) OR (\"Source\" = 'UgcCustomerOffer' AND \"PromotionId\" IS NULL AND \"CreatorAllocationId\" IS NULL AND \"UgcCustomerOfferId\" IS NOT NULL AND ((\"CreatorId\" IS NOT NULL AND \"UgcAssignmentId\" IS NOT NULL) OR (\"CreatorId\" IS NULL AND \"UgcAssignmentId\" IS NULL)))");

            migrationBuilder.AddForeignKey(
                name: "FK_OfferQrSessions_UgcAssignments_UgcAssignmentId",
                schema: "v3",
                table: "OfferQrSessions",
                column: "UgcAssignmentId",
                principalSchema: "v3",
                principalTable: "UgcAssignments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_UgcCustomerOfferSales_UgcAssignments_UgcAssignmentId",
                schema: "v3",
                table: "UgcCustomerOfferSales",
                column: "UgcAssignmentId",
                principalSchema: "v3",
                principalTable: "UgcAssignments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // Historical UGC QR/sale rows retain NULL attribution. The check allows that
            // history; this insert guard requires the new binding for every new QR.
            migrationBuilder.Sql("""
                CREATE FUNCTION v3.guard_ugc_assignment_qr() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  IF TG_OP='INSERT' THEN
                    IF NEW."Source"='UgcCustomerOffer' AND (
                      NEW."CreatorId" IS NULL OR NEW."UgcAssignmentId" IS NULL OR
                      NOT EXISTS (SELECT 1 FROM v3."UgcAssignments" a
                        JOIN v3."UgcCustomerOffers" o ON o."UgcOpportunityId"=a."UgcOpportunityId"
                        JOIN v3."UgcCreatorRequests" r ON r."Id"=a."UgcCreatorRequestId"
                        WHERE a."Id"=NEW."UgcAssignmentId" AND a."CreatorId"=NEW."CreatorId"
                          AND o."Id"=NEW."UgcCustomerOfferId" AND o."BusinessId"=NEW."BusinessId"
                          AND r."CreatorId"=a."CreatorId" AND r."UgcOpportunityId"=a."UgcOpportunityId"
                          AND r."Status"='Approved' AND a."Status"<>'Rejected')) THEN
                      RAISE EXCEPTION 'New UGC QR requires its exact eligible Creator assignment' USING ERRCODE='23514';
                    END IF;
                    RETURN NEW;
                  END IF;
                  IF NEW."UgcAssignmentId" IS DISTINCT FROM OLD."UgcAssignmentId" THEN
                    RAISE EXCEPTION 'UGC QR assignment is immutable' USING ERRCODE='23514';
                  END IF;
                  IF NEW."Source"='UgcCustomerOffer' AND NEW."Status"='Used' AND OLD."Status"<>'Used' AND (
                    OLD."UgcAssignmentId" IS NULL OR NOT EXISTS (
                      SELECT 1 FROM v3."UgcCustomerOfferSales" s
                      WHERE s."Id"=NEW."UgcCustomerOfferSaleId" AND s."UgcAssignmentId"=OLD."UgcAssignmentId"
                        AND s."UgcCustomerOfferId"=OLD."UgcCustomerOfferId")) THEN
                    RAISE EXCEPTION 'UGC QR sale must retain the same Creator assignment' USING ERRCODE='23514';
                  END IF;
                  RETURN NEW;
                END $$;
                REVOKE ALL ON FUNCTION v3.guard_ugc_assignment_qr() FROM PUBLIC;
                CREATE TRIGGER ugc_assignment_qr_guard BEFORE INSERT OR UPDATE ON v3."OfferQrSessions"
                  FOR EACH ROW EXECUTE FUNCTION v3.guard_ugc_assignment_qr();

                CREATE OR REPLACE FUNCTION v3.guard_ugc_customer_offer_sale() RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE offer record;
                BEGIN
                  SELECT * INTO offer FROM v3."UgcCustomerOffers" WHERE "Id"=NEW."UgcCustomerOfferId";
                  IF NEW."UgcAssignmentId" IS NULL OR offer IS NULL OR
                     offer."UgcOpportunityId"<>NEW."UgcOpportunityId" OR offer."BusinessId"<>NEW."BusinessId" OR
                     NOT EXISTS (SELECT 1 FROM v3."UgcAssignments" a
                       JOIN v3."UgcCreatorRequests" r ON r."Id"=a."UgcCreatorRequestId"
                       WHERE a."Id"=NEW."UgcAssignmentId" AND a."UgcOpportunityId"=NEW."UgcOpportunityId"
                         AND r."CreatorId"=a."CreatorId" AND r."UgcOpportunityId"=a."UgcOpportunityId"
                         AND r."Status"='Approved' AND a."Status"<>'Rejected') OR
                     NEW."CustomerDiscountAmount"<>round(NEW."PurchaseAmount"*offer."CustomerDiscountPercent"/100,2) OR
                     NEW."PlatformRevenueAmount"<>round(NEW."PurchaseAmount"*offer."PricingSnapshot_PlatformSalePercent"/100,2) OR
                     NOT (
                       (NEW."QrTokenReference"='manual:'||NEW."UgcCustomerOfferId"::text) OR
                       EXISTS (SELECT 1 FROM v3."OfferQrSessions" q WHERE q."Id"::text=NEW."QrTokenReference" AND
                         q."Source"='UgcCustomerOffer' AND q."Status"='Issued' AND
                         q."UgcCustomerOfferId"=NEW."UgcCustomerOfferId" AND q."BusinessId"=NEW."BusinessId" AND
                         q."CustomerId"=NEW."CustomerId" AND q."UgcAssignmentId"=NEW."UgcAssignmentId" AND
                         q."CreatorId"=(SELECT a."CreatorId" FROM v3."UgcAssignments" a WHERE a."Id"=NEW."UgcAssignmentId"))
                     ) THEN
                    RAISE EXCEPTION 'UGC Sale must match its offer and exact Creator assignment' USING ERRCODE='23514';
                  END IF;
                  RETURN NEW;
                END $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Rolling schema back after an attributed purchase would discard immutable
            // financial evidence. A clean disposable database can still roll back/reapply.
            migrationBuilder.Sql("""
                DO $$ BEGIN
                  IF EXISTS (SELECT 1 FROM v3."OfferQrSessions" WHERE "UgcAssignmentId" IS NOT NULL)
                     OR EXISTS (SELECT 1 FROM v3."UgcCustomerOfferSales" WHERE "UgcAssignmentId" IS NOT NULL) THEN
                    RAISE EXCEPTION 'Attributed UGC activity requires forward recovery or reviewed backup restore';
                  END IF;
                END $$;
                DROP TRIGGER ugc_assignment_qr_guard ON v3."OfferQrSessions";
                DROP FUNCTION v3.guard_ugc_assignment_qr();
                CREATE OR REPLACE FUNCTION v3.guard_ugc_customer_offer_sale() RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE offer record;
                BEGIN
                  SELECT * INTO offer FROM v3."UgcCustomerOffers" WHERE "Id"=NEW."UgcCustomerOfferId";
                  IF offer IS NULL OR offer."UgcOpportunityId"<>NEW."UgcOpportunityId" OR offer."BusinessId"<>NEW."BusinessId" OR
                     NEW."CustomerDiscountAmount"<>round(NEW."PurchaseAmount"*offer."CustomerDiscountPercent"/100,2) OR
                     NEW."PlatformRevenueAmount"<>round(NEW."PurchaseAmount"*offer."PricingSnapshot_PlatformSalePercent"/100,2) OR
                     NOT EXISTS (SELECT 1 FROM v3."OfferQrSessions" q WHERE q."Id"::text=NEW."QrTokenReference" AND
                       q."Source"='UgcCustomerOffer' AND q."Status"='Issued' AND q."UgcCustomerOfferId"=NEW."UgcCustomerOfferId" AND
                       q."BusinessId"=NEW."BusinessId" AND q."CustomerId"=NEW."CustomerId") THEN
                    RAISE EXCEPTION 'UGC Customer Offer Sale must match its immutable offer snapshot and issued QR' USING ERRCODE='23514';
                  END IF;
                  RETURN NEW;
                END $$;
                """);
            migrationBuilder.DropForeignKey(
                name: "FK_OfferQrSessions_UgcAssignments_UgcAssignmentId",
                schema: "v3",
                table: "OfferQrSessions");

            migrationBuilder.DropForeignKey(
                name: "FK_UgcCustomerOfferSales_UgcAssignments_UgcAssignmentId",
                schema: "v3",
                table: "UgcCustomerOfferSales");

            migrationBuilder.DropIndex(
                name: "IX_UgcCustomerOfferSales_UgcAssignmentId",
                schema: "v3",
                table: "UgcCustomerOfferSales");

            migrationBuilder.DropIndex(
                name: "IX_OfferQrSessions_UgcAssignmentId",
                schema: "v3",
                table: "OfferQrSessions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Qr_SourceBinding",
                schema: "v3",
                table: "OfferQrSessions");

            migrationBuilder.DropColumn(
                name: "UgcAssignmentId",
                schema: "v3",
                table: "UgcCustomerOfferSales");

            migrationBuilder.DropColumn(
                name: "UgcAssignmentId",
                schema: "v3",
                table: "OfferQrSessions");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Qr_SourceBinding",
                schema: "v3",
                table: "OfferQrSessions",
                sql: "(\"Source\" = 'ViewAndSalePromotion' AND \"PromotionId\" IS NOT NULL AND \"CreatorId\" IS NOT NULL AND \"CreatorAllocationId\" IS NOT NULL AND \"UgcCustomerOfferId\" IS NULL) OR (\"Source\" = 'UgcCustomerOffer' AND \"PromotionId\" IS NULL AND \"CreatorId\" IS NULL AND \"CreatorAllocationId\" IS NULL AND \"UgcCustomerOfferId\" IS NOT NULL)");
        }
    }
}
