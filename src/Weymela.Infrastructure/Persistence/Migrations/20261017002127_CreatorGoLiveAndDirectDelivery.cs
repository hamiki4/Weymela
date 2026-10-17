using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Weymela.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CreatorGoLiveAndDirectDelivery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Publication_Provider",
                schema: "v3",
                table: "CreatorPublicationVerifications");

            migrationBuilder.AddColumn<string>(
                name: "DeliverableType",
                schema: "v3",
                table: "UgcOpportunities",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "PublicSocialVideo");

            migrationBuilder.AddColumn<int>(
                name: "PromotionLiveDurationDays",
                schema: "v3",
                table: "UgcOpportunities",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ExpiresAtUtc",
                schema: "v3",
                table: "CreatorPublicationVerifications",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "GoLiveRequestedAtUtc",
                schema: "v3",
                table: "CreatorPublicationVerifications",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LiveDurationDays",
                schema: "v3",
                table: "CreatorPublicationVerifications",
                type: "integer",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_UgcOpportunity_Delivery",
                schema: "v3",
                table: "UgcOpportunities",
                sql: "\"DeliverableType\" IN ('PublicSocialVideo','DirectVideoDelivery') AND (\"DeliverableType\" <> 'DirectVideoDelivery' OR \"ContentType\" = 'Video')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Publication_Duration",
                schema: "v3",
                table: "CreatorPublicationVerifications",
                sql: "\"LiveDurationDays\" IS NULL OR (\"LiveDurationDays\" BETWEEN 1 AND 365 AND \"ExpiresAtUtc\" = \"WentLiveAtUtc\" + \"LiveDurationDays\" * interval '1 day')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Publication_Provider",
                schema: "v3",
                table: "CreatorPublicationVerifications",
                sql: "\"Provider\" IN ('TikTok','YouTube','Instagram','Facebook')");
            migrationBuilder.Sql(CreatorDeliveryIntegritySql.Up);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                  IF EXISTS (SELECT 1 FROM v3."UgcOpportunities" WHERE "DeliverableType"='DirectVideoDelivery' OR ("ApprovedCreatorCount">0 AND "PromotionLiveDurationDays" IS NOT NULL))
                    OR EXISTS (SELECT 1 FROM v3."CreatorPublicationVerifications" WHERE "GoLiveRequestedAtUtc" IS NOT NULL OR "ExpiresAtUtc" IS NOT NULL) THEN
                    RAISE EXCEPTION 'Agreed delivery terms and Go Live evidence require roll-forward recovery';
                  END IF;
                END $$;
                DROP INDEX v3."UX_UgcPublication_StartedOnce";
                DROP INDEX v3."UX_PromotionPublication_StartedOnce";
                DROP TRIGGER direct_delivery_correction ON v3."UgcPaymentCorrections";
                DROP FUNCTION v3.reject_direct_delivery_correction();
                DROP TRIGGER direct_delivery_scope ON v3."UgcOpportunities";
                DROP TRIGGER direct_offer_scope ON v3."UgcCustomerOffers";
                DROP TRIGGER direct_requirement_scope ON v3."UgcPlatformRequirements";
                DROP TRIGGER direct_capacity_scope ON v3."UgcPlatformCapacities";
                DROP FUNCTION v3.check_direct_delivery_scope();
                DROP TRIGGER creator_live_terms ON v3."CreatorPublicationVerifications";
                DROP TRIGGER ugc_delivery_terms ON v3."UgcOpportunities";
                DROP FUNCTION v3.guard_creator_delivery_terms();
                """);
            var original = StabilizationIntegritySql.Up;
            migrationBuilder.Sql(original[original.IndexOf("CREATE FUNCTION v3.check_ugc_publication_earning()")..original.IndexOf("CREATE CONSTRAINT TRIGGER ugc_verified_fixed_earning")]
                .Replace("CREATE FUNCTION", "CREATE OR REPLACE FUNCTION"));
            migrationBuilder.DropCheckConstraint(name: "CK_UgcOpportunity_Delivery", schema: "v3", table: "UgcOpportunities");
            migrationBuilder.DropCheckConstraint(name: "CK_Publication_Duration", schema: "v3", table: "CreatorPublicationVerifications");
            migrationBuilder.DropCheckConstraint(name: "CK_Publication_Provider", schema: "v3", table: "CreatorPublicationVerifications");
            migrationBuilder.DropColumn(name: "DeliverableType", schema: "v3", table: "UgcOpportunities");
            migrationBuilder.DropColumn(name: "PromotionLiveDurationDays", schema: "v3", table: "UgcOpportunities");
            migrationBuilder.DropColumn(name: "ExpiresAtUtc", schema: "v3", table: "CreatorPublicationVerifications");
            migrationBuilder.DropColumn(name: "GoLiveRequestedAtUtc", schema: "v3", table: "CreatorPublicationVerifications");
            migrationBuilder.DropColumn(name: "LiveDurationDays", schema: "v3", table: "CreatorPublicationVerifications");
            migrationBuilder.AddCheckConstraint(name: "CK_Publication_Provider", schema: "v3", table: "CreatorPublicationVerifications", sql: "\"Provider\" IN ('TikTok','YouTube','Instagram')");
        }
    }
}
