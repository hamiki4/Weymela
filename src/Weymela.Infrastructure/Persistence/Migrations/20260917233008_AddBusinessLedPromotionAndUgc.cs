using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Weymela.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBusinessLedPromotionAndUgc : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "UgcOpportunityId",
                schema: "v3",
                table: "WalletEntries",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Location",
                schema: "v3",
                table: "Promotions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ResourcesJson",
                schema: "v3",
                table: "Promotions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Slogan",
                schema: "v3",
                table: "Promotions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "UgcAssignmentId",
                schema: "v3",
                table: "PlatformRevenueEntries",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "UgcAssignmentId",
                schema: "v3",
                table: "FinancialJournals",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "UgcOpportunityId",
                schema: "v3",
                table: "FinancialJournals",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "Ugc_ConfigurationVersionId",
                schema: "v3",
                table: "FinancialConfigurationVersions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "Ugc_EffectiveFromUtc",
                schema: "v3",
                table: "FinancialConfigurationVersions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Ugc_MinimumCreatorPayment",
                schema: "v3",
                table: "FinancialConfigurationVersions",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Ugc_MinimumUgcBudget",
                schema: "v3",
                table: "FinancialConfigurationVersions",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Ugc_PlatformFeePercent",
                schema: "v3",
                table: "FinancialConfigurationVersions",
                type: "numeric(9,4)",
                precision: 9,
                scale: 4,
                nullable: true);

            // Financial history is append-only. Leave Version 1 byte-for-byte
            // unchanged in its historical columns (the new UGC columns stay NULL)
            // and introduce UGC through the normal next-version snapshot.
            migrationBuilder.Sql("""
                DO $$
                DECLARE
                    configuration_count integer;
                    version_count integer;
                BEGIN
                    SELECT count(*) INTO configuration_count
                    FROM v3."FinancialConfigurations"
                    WHERE "Name" = 'PlatformPricing';

                    SELECT count(*) INTO version_count
                    FROM v3."FinancialConfigurationVersions" v
                    JOIN v3."FinancialConfigurations" c ON c."Id" = v."ConfigurationId"
                    WHERE c."Name" = 'PlatformPricing';

                    IF configuration_count = 0 AND version_count = 0 THEN
                        RETURN;
                    END IF;

                    IF configuration_count <> 1 OR version_count <> 1 OR NOT EXISTS (
                        SELECT 1
                        FROM v3."FinancialConfigurationVersions" v
                        JOIN v3."FinancialConfigurations" c ON c."Id" = v."ConfigurationId"
                        WHERE c."Name" = 'PlatformPricing' AND v."Version" = 1
                    ) THEN
                        RAISE EXCEPTION 'UGC configuration migration requires the governed PlatformPricing Version 1 baseline';
                    END IF;
                END $$;

                WITH baseline AS (
                    SELECT v.*
                    FROM v3."FinancialConfigurationVersions" v
                    JOIN v3."FinancialConfigurations" c ON c."Id" = v."ConfigurationId"
                    WHERE c."Name" = 'PlatformPricing' AND v."Version" = 1
                ), next_version AS (
                    SELECT
                        (
                            substr(md5(v."Id"::text || ':ugc-version-2'), 1, 8) || '-' ||
                            substr(md5(v."Id"::text || ':ugc-version-2'), 9, 4) || '-' ||
                            substr(md5(v."Id"::text || ':ugc-version-2'), 13, 4) || '-' ||
                            substr(md5(v."Id"::text || ':ugc-version-2'), 17, 4) || '-' ||
                            substr(md5(v."Id"::text || ':ugc-version-2'), 21, 12)
                        )::uuid AS "NewId",
                        transaction_timestamp() AS "NewEffectiveFromUtc",
                        v.*
                    FROM baseline v
                )
                INSERT INTO v3."FinancialConfigurationVersions" (
                    "Id", "ConfigurationId", "Version", "ChangedBy", "EffectiveFromUtc",
                    "ViewOnly_PromotionType", "ViewOnly_ViewsPerReward", "ViewOnly_BusinessCharge",
                    "ViewOnly_CreatorEarning", "ViewOnly_PlatformEarning", "ViewOnly_CreatorCommissionPercent",
                    "ViewOnly_CustomerCashbackPercent", "ViewOnly_PlatformPercent", "ViewOnly_EffectiveFromUtc",
                    "ViewOnly_ConfigurationVersionId", "ViewOnly_MinimumPromotionBudget",
                    "ViewPlusCommission_PromotionType", "ViewPlusCommission_ViewsPerReward",
                    "ViewPlusCommission_BusinessCharge", "ViewPlusCommission_CreatorEarning",
                    "ViewPlusCommission_PlatformEarning", "ViewPlusCommission_CreatorCommissionPercent",
                    "ViewPlusCommission_CustomerCashbackPercent", "ViewPlusCommission_PlatformPercent",
                    "ViewPlusCommission_EffectiveFromUtc", "ViewPlusCommission_ConfigurationVersionId",
                    "ViewPlusCommission_MinimumPromotionBudget", "CreatorPayoutThreshold", "CustomerPayoutThreshold",
                    "Ugc_ConfigurationVersionId", "Ugc_EffectiveFromUtc", "Ugc_MinimumCreatorPayment",
                    "Ugc_MinimumUgcBudget", "Ugc_PlatformFeePercent"
                )
                SELECT
                    "NewId", "ConfigurationId", 2, "ChangedBy", "NewEffectiveFromUtc",
                    "ViewOnly_PromotionType", "ViewOnly_ViewsPerReward", "ViewOnly_BusinessCharge",
                    "ViewOnly_CreatorEarning", "ViewOnly_PlatformEarning", "ViewOnly_CreatorCommissionPercent",
                    "ViewOnly_CustomerCashbackPercent", "ViewOnly_PlatformPercent", "NewEffectiveFromUtc",
                    "NewId", "ViewOnly_MinimumPromotionBudget",
                    "ViewPlusCommission_PromotionType", "ViewPlusCommission_ViewsPerReward",
                    "ViewPlusCommission_BusinessCharge", "ViewPlusCommission_CreatorEarning",
                    "ViewPlusCommission_PlatformEarning", "ViewPlusCommission_CreatorCommissionPercent",
                    "ViewPlusCommission_CustomerCashbackPercent", "ViewPlusCommission_PlatformPercent",
                    "NewEffectiveFromUtc", "NewId", "ViewPlusCommission_MinimumPromotionBudget",
                    "CreatorPayoutThreshold", "CustomerPayoutThreshold",
                    "NewId", "NewEffectiveFromUtc", 200.00, NULL, 10.0000
                FROM next_version;
                """);

            // Extend the existing authoritative wallet/journal projection for the
            // additive UGC reserve account. This replaces the function for existing
            // databases; fresh databases receive the same definition from IntegritySql.
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION v3.check_wallet_journal() RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE bid uuid; available numeric; reserved numeric; actual_available numeric; actual_reserved numeric;
                BEGIN
                  bid := NEW."BusinessId";
                  IF bid IS NULL THEN RETURN NULL; END IF;
                  SELECT coalesce(sum(CASE WHEN l."Account"='BusinessAvailable' THEN
                    CASE WHEN l."Type"='Credit' THEN l."Amount" ELSE -l."Amount" END ELSE 0 END),0),
                    coalesce(sum(CASE WHEN l."Account" IN ('CampaignUnallocatedReserve','CreatorAllocatedReserve','UgcAllocatedReserve') THEN
                    CASE WHEN l."Type"='Credit' THEN l."Amount" ELSE -l."Amount" END ELSE 0 END),0)
                  INTO available, reserved FROM v3."FinancialJournalLines" l JOIN v3."FinancialJournals" j ON j."Id"=l."JournalId"
                  WHERE j."BusinessId"=bid;
                  SELECT "AvailableBalance", "ReservedBalance" INTO actual_available, actual_reserved
                    FROM v3."BusinessWallets" WHERE "BusinessId"=bid;
                  IF actual_available IS NULL OR actual_available <> available OR actual_reserved <> reserved THEN
                    RAISE EXCEPTION 'Business wallet must reconcile with authoritative journals' USING ERRCODE='23514';
                  END IF;
                  RETURN NULL;
                END $$;
                """);

            migrationBuilder.AddColumn<Guid>(
                name: "UgcAssignmentId",
                schema: "v3",
                table: "CreatorEarningEntries",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CreatorSocialProfileId",
                schema: "v3",
                table: "CreatorApplications",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Platform",
                schema: "v3",
                table: "CreatorApplications",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CreatorSocialProfileId",
                schema: "v3",
                table: "CreatorAllocations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Platform",
                schema: "v3",
                table: "CreatorAllocations",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "UgcOpportunityId",
                schema: "v3",
                table: "AuditEvents",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AdminGrants",
                schema: "v3",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    DisplayName = table.Column<string>(type: "text", nullable: false),
                    Role = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    GrantedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    GrantedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RevokedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    RevokedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastActivityAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdminGrants", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CreatorSocialProfiles",
                schema: "v3",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: false),
                    Platform = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ProfileUrl = table.Column<string>(type: "text", nullable: false),
                    SelfReportedAudience = table.Column<long>(type: "bigint", nullable: false),
                    VerificationStatus = table.Column<string>(type: "text", nullable: false),
                    VerifiedAudience = table.Column<long>(type: "bigint", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CreatorSocialProfiles", x => x.Id);
                    table.CheckConstraint("CK_CreatorSocialProfile_Audience", "\"SelfReportedAudience\" >= 0 AND (\"VerifiedAudience\" IS NULL OR \"VerifiedAudience\" >= 0)");
                });

            migrationBuilder.CreateTable(
                name: "PromotionPlatforms",
                schema: "v3",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PromotionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Platform = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Capacity = table.Column<int>(type: "integer", nullable: false),
                    ApprovedCount = table.Column<int>(type: "integer", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PromotionPlatforms", x => x.Id);
                    table.CheckConstraint("CK_PromotionPlatform_Capacity", "\"Capacity\" > 0 AND \"ApprovedCount\" >= 0 AND \"ApprovedCount\" <= \"Capacity\"");
                    table.ForeignKey(
                        name: "FK_PromotionPlatforms_Promotions_PromotionId",
                        column: x => x.PromotionId,
                        principalSchema: "v3",
                        principalTable: "Promotions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "UgcOpportunities",
                schema: "v3",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "text", nullable: false),
                    Slogan = table.Column<string>(type: "text", nullable: true),
                    ContentType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Instructions = table.Column<string>(type: "text", nullable: false),
                    ResourcesJson = table.Column<string>(type: "text", nullable: false),
                    Location = table.Column<string>(type: "text", nullable: true),
                    DueDateUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ProductProvided = table.Column<bool>(type: "boolean", nullable: false),
                    CreatorMustPurchase = table.Column<bool>(type: "boolean", nullable: false),
                    UsageRights = table.Column<string>(type: "text", nullable: true),
                    CreatorPayment = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    CreatorCapacity = table.Column<int>(type: "integer", nullable: false),
                    ApprovedCreatorCount = table.Column<int>(type: "integer", nullable: false),
                    RequiredFunding = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ReservedFunding = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    UsedFunding = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    PricingSnapshot_MinimumCreatorPayment = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    PricingSnapshot_PlatformFeePercent = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: false),
                    PricingSnapshot_MinimumUgcBudget = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    PricingSnapshot_EffectiveFromUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PricingSnapshot_ConfigurationVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CurrentRevision = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PublishedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UgcOpportunities", x => x.Id);
                    table.CheckConstraint("CK_UgcOpportunity_Capacity", "\"CreatorCapacity\" > 0 AND \"ApprovedCreatorCount\" >= 0 AND \"ApprovedCreatorCount\" <= \"CreatorCapacity\"");
                    table.CheckConstraint("CK_UgcOpportunity_Funding", "\"RequiredFunding\" > 0 AND \"ReservedFunding\" >= 0 AND \"UsedFunding\" >= 0 AND \"ReservedFunding\" + \"UsedFunding\" <= \"RequiredFunding\"");
                    table.ForeignKey(
                        name: "FK_UgcOpportunities_BusinessWallets_BusinessId",
                        column: x => x.BusinessId,
                        principalSchema: "v3",
                        principalTable: "BusinessWallets",
                        principalColumn: "BusinessId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "UgcCreatorRequests",
                schema: "v3",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UgcOpportunityId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RequestedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ReviewedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReviewedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    RejectionReason = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UgcCreatorRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UgcCreatorRequests_UgcOpportunities_UgcOpportunityId",
                        column: x => x.UgcOpportunityId,
                        principalSchema: "v3",
                        principalTable: "UgcOpportunities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "UgcPlatformRequirements",
                schema: "v3",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UgcOpportunityId = table.Column<Guid>(type: "uuid", nullable: false),
                    Platform = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Format = table.Column<string>(type: "text", nullable: false),
                    MinimumAudience = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UgcPlatformRequirements", x => x.Id);
                    table.CheckConstraint("CK_UgcPlatformRequirement_Audience", "\"MinimumAudience\" IS NULL OR \"MinimumAudience\" >= 0");
                    table.ForeignKey(
                        name: "FK_UgcPlatformRequirements_UgcOpportunities_UgcOpportunityId",
                        column: x => x.UgcOpportunityId,
                        principalSchema: "v3",
                        principalTable: "UgcOpportunities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "UgcReservations",
                schema: "v3",
                columns: table => new
                {
                    UgcOpportunityId = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uuid", nullable: false),
                    OriginalAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    JournalId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UgcReservations", x => x.UgcOpportunityId);
                    table.CheckConstraint("CK_UgcReservation_Positive", "\"OriginalAmount\" > 0");
                    table.ForeignKey(
                        name: "FK_UgcReservations_BusinessWallets_BusinessId",
                        column: x => x.BusinessId,
                        principalSchema: "v3",
                        principalTable: "BusinessWallets",
                        principalColumn: "BusinessId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UgcReservations_FinancialJournals_JournalId",
                        column: x => x.JournalId,
                        principalSchema: "v3",
                        principalTable: "FinancialJournals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UgcReservations_UgcOpportunities_UgcOpportunityId",
                        column: x => x.UgcOpportunityId,
                        principalSchema: "v3",
                        principalTable: "UgcOpportunities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "UgcRevisions",
                schema: "v3",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UgcOpportunityId = table.Column<Guid>(type: "uuid", nullable: false),
                    RevisionNumber = table.Column<int>(type: "integer", nullable: false),
                    IsMaterial = table.Column<bool>(type: "boolean", nullable: false),
                    SnapshotJson = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UgcRevisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UgcRevisions_UgcOpportunities_UgcOpportunityId",
                        column: x => x.UgcOpportunityId,
                        principalSchema: "v3",
                        principalTable: "UgcOpportunities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "UgcAssignments",
                schema: "v3",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UgcOpportunityId = table.Column<Guid>(type: "uuid", nullable: false),
                    UgcCreatorRequestId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatorPayment = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    PlatformFee = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    AcceptedRevisionNumber = table.Column<int>(type: "integer", nullable: false),
                    RevisionAcceptanceRequired = table.Column<bool>(type: "boolean", nullable: false),
                    Status = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ApprovedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UgcAssignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UgcAssignments_UgcCreatorRequests_UgcCreatorRequestId",
                        column: x => x.UgcCreatorRequestId,
                        principalSchema: "v3",
                        principalTable: "UgcCreatorRequests",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UgcAssignments_UgcOpportunities_UgcOpportunityId",
                        column: x => x.UgcOpportunityId,
                        principalSchema: "v3",
                        principalTable: "UgcOpportunities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "UgcBudgetEntries",
                schema: "v3",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UgcOpportunityId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssignmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Movement = table.Column<string>(type: "text", nullable: false),
                    JournalId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UgcBudgetEntries", x => x.Id);
                    table.CheckConstraint("CK_UgcBudgetEntry_Positive", "\"Amount\" > 0");
                    table.ForeignKey(
                        name: "FK_UgcBudgetEntries_FinancialJournals_JournalId",
                        column: x => x.JournalId,
                        principalSchema: "v3",
                        principalTable: "FinancialJournals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UgcBudgetEntries_UgcAssignments_AssignmentId",
                        column: x => x.AssignmentId,
                        principalSchema: "v3",
                        principalTable: "UgcAssignments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UgcBudgetEntries_UgcOpportunities_UgcOpportunityId",
                        column: x => x.UgcOpportunityId,
                        principalSchema: "v3",
                        principalTable: "UgcOpportunities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "UgcSubmissions",
                schema: "v3",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UgcAssignmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    RevisionNumber = table.Column<int>(type: "integer", nullable: false),
                    SubmissionUrl = table.Column<string>(type: "text", nullable: false),
                    SubmittedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ReviewedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReviewedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Feedback = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UgcSubmissions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UgcSubmissions_UgcAssignments_UgcAssignmentId",
                        column: x => x.UgcAssignmentId,
                        principalSchema: "v3",
                        principalTable: "UgcAssignments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PlatformRevenueEntries_UgcAssignmentId",
                schema: "v3",
                table: "PlatformRevenueEntries",
                column: "UgcAssignmentId");

            migrationBuilder.CreateIndex(
                name: "IX_FinancialJournals_UgcAssignmentId",
                schema: "v3",
                table: "FinancialJournals",
                column: "UgcAssignmentId");

            migrationBuilder.CreateIndex(
                name: "IX_FinancialJournals_UgcOpportunityId",
                schema: "v3",
                table: "FinancialJournals",
                column: "UgcOpportunityId");

            migrationBuilder.CreateIndex(
                name: "IX_CreatorEarningEntries_UgcAssignmentId",
                schema: "v3",
                table: "CreatorEarningEntries",
                column: "UgcAssignmentId");

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_UgcOpportunityId_OccurredAtUtc",
                schema: "v3",
                table: "AuditEvents",
                columns: new[] { "UgcOpportunityId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AdminGrants_UserId_Role",
                schema: "v3",
                table: "AdminGrants",
                columns: new[] { "UserId", "Role" },
                unique: true,
                filter: "\"IsActive\"");

            migrationBuilder.CreateIndex(
                name: "IX_CreatorSocialProfiles_CreatorId_Platform",
                schema: "v3",
                table: "CreatorSocialProfiles",
                columns: new[] { "CreatorId", "Platform" },
                unique: true,
                filter: "\"IsActive\"");

            migrationBuilder.CreateIndex(
                name: "IX_PromotionPlatforms_PromotionId_Platform",
                schema: "v3",
                table: "PromotionPlatforms",
                columns: new[] { "PromotionId", "Platform" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UgcAssignments_UgcCreatorRequestId",
                schema: "v3",
                table: "UgcAssignments",
                column: "UgcCreatorRequestId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UgcAssignments_UgcOpportunityId_CreatorId",
                schema: "v3",
                table: "UgcAssignments",
                columns: new[] { "UgcOpportunityId", "CreatorId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UgcBudgetEntries_AssignmentId",
                schema: "v3",
                table: "UgcBudgetEntries",
                column: "AssignmentId");

            migrationBuilder.CreateIndex(
                name: "IX_UgcBudgetEntries_JournalId",
                schema: "v3",
                table: "UgcBudgetEntries",
                column: "JournalId");

            migrationBuilder.CreateIndex(
                name: "IX_UgcBudgetEntries_UgcOpportunityId_CreatedAtUtc",
                schema: "v3",
                table: "UgcBudgetEntries",
                columns: new[] { "UgcOpportunityId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_UgcCreatorRequests_UgcOpportunityId_CreatorId",
                schema: "v3",
                table: "UgcCreatorRequests",
                columns: new[] { "UgcOpportunityId", "CreatorId" },
                unique: true,
                filter: "\"Status\" IN ('Pending','Approved')");

            migrationBuilder.CreateIndex(
                name: "IX_UgcCreatorRequests_UgcOpportunityId_Status",
                schema: "v3",
                table: "UgcCreatorRequests",
                columns: new[] { "UgcOpportunityId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_UgcOpportunities_BusinessId_Status",
                schema: "v3",
                table: "UgcOpportunities",
                columns: new[] { "BusinessId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_UgcPlatformRequirements_UgcOpportunityId_Platform",
                schema: "v3",
                table: "UgcPlatformRequirements",
                columns: new[] { "UgcOpportunityId", "Platform" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UgcReservations_BusinessId",
                schema: "v3",
                table: "UgcReservations",
                column: "BusinessId");

            migrationBuilder.CreateIndex(
                name: "IX_UgcReservations_JournalId",
                schema: "v3",
                table: "UgcReservations",
                column: "JournalId");

            migrationBuilder.CreateIndex(
                name: "IX_UgcRevisions_UgcOpportunityId_RevisionNumber",
                schema: "v3",
                table: "UgcRevisions",
                columns: new[] { "UgcOpportunityId", "RevisionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UgcSubmissions_UgcAssignmentId_SubmittedAtUtc",
                schema: "v3",
                table: "UgcSubmissions",
                columns: new[] { "UgcAssignmentId", "SubmittedAtUtc" });

            migrationBuilder.AddForeignKey(
                name: "FK_CreatorEarningEntries_UgcAssignments_UgcAssignmentId",
                schema: "v3",
                table: "CreatorEarningEntries",
                column: "UgcAssignmentId",
                principalSchema: "v3",
                principalTable: "UgcAssignments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_PlatformRevenueEntries_UgcAssignments_UgcAssignmentId",
                schema: "v3",
                table: "PlatformRevenueEntries",
                column: "UgcAssignmentId",
                principalSchema: "v3",
                principalTable: "UgcAssignments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CreatorEarningEntries_UgcAssignments_UgcAssignmentId",
                schema: "v3",
                table: "CreatorEarningEntries");

            migrationBuilder.DropForeignKey(
                name: "FK_PlatformRevenueEntries_UgcAssignments_UgcAssignmentId",
                schema: "v3",
                table: "PlatformRevenueEntries");

            migrationBuilder.DropTable(
                name: "AdminGrants",
                schema: "v3");

            migrationBuilder.DropTable(
                name: "CreatorSocialProfiles",
                schema: "v3");

            migrationBuilder.DropTable(
                name: "PromotionPlatforms",
                schema: "v3");

            migrationBuilder.DropTable(
                name: "UgcBudgetEntries",
                schema: "v3");

            migrationBuilder.DropTable(
                name: "UgcPlatformRequirements",
                schema: "v3");

            migrationBuilder.DropTable(
                name: "UgcReservations",
                schema: "v3");

            migrationBuilder.DropTable(
                name: "UgcRevisions",
                schema: "v3");

            migrationBuilder.DropTable(
                name: "UgcSubmissions",
                schema: "v3");

            migrationBuilder.DropTable(
                name: "UgcAssignments",
                schema: "v3");

            migrationBuilder.DropTable(
                name: "UgcCreatorRequests",
                schema: "v3");

            migrationBuilder.DropTable(
                name: "UgcOpportunities",
                schema: "v3");

            migrationBuilder.DropIndex(
                name: "IX_PlatformRevenueEntries_UgcAssignmentId",
                schema: "v3",
                table: "PlatformRevenueEntries");

            migrationBuilder.DropIndex(
                name: "IX_FinancialJournals_UgcAssignmentId",
                schema: "v3",
                table: "FinancialJournals");

            migrationBuilder.DropIndex(
                name: "IX_FinancialJournals_UgcOpportunityId",
                schema: "v3",
                table: "FinancialJournals");

            migrationBuilder.DropIndex(
                name: "IX_CreatorEarningEntries_UgcAssignmentId",
                schema: "v3",
                table: "CreatorEarningEntries");

            migrationBuilder.DropIndex(
                name: "IX_AuditEvents_UgcOpportunityId_OccurredAtUtc",
                schema: "v3",
                table: "AuditEvents");

            migrationBuilder.DropColumn(
                name: "UgcOpportunityId",
                schema: "v3",
                table: "WalletEntries");

            migrationBuilder.DropColumn(
                name: "Location",
                schema: "v3",
                table: "Promotions");

            migrationBuilder.DropColumn(
                name: "ResourcesJson",
                schema: "v3",
                table: "Promotions");

            migrationBuilder.DropColumn(
                name: "Slogan",
                schema: "v3",
                table: "Promotions");

            migrationBuilder.DropColumn(
                name: "UgcAssignmentId",
                schema: "v3",
                table: "PlatformRevenueEntries");

            migrationBuilder.DropColumn(
                name: "UgcAssignmentId",
                schema: "v3",
                table: "FinancialJournals");

            migrationBuilder.DropColumn(
                name: "UgcOpportunityId",
                schema: "v3",
                table: "FinancialJournals");

            migrationBuilder.DropColumn(
                name: "Ugc_ConfigurationVersionId",
                schema: "v3",
                table: "FinancialConfigurationVersions");

            migrationBuilder.DropColumn(
                name: "Ugc_EffectiveFromUtc",
                schema: "v3",
                table: "FinancialConfigurationVersions");

            migrationBuilder.DropColumn(
                name: "Ugc_MinimumCreatorPayment",
                schema: "v3",
                table: "FinancialConfigurationVersions");

            migrationBuilder.DropColumn(
                name: "Ugc_MinimumUgcBudget",
                schema: "v3",
                table: "FinancialConfigurationVersions");

            migrationBuilder.DropColumn(
                name: "Ugc_PlatformFeePercent",
                schema: "v3",
                table: "FinancialConfigurationVersions");

            migrationBuilder.DropColumn(
                name: "UgcAssignmentId",
                schema: "v3",
                table: "CreatorEarningEntries");

            migrationBuilder.DropColumn(
                name: "CreatorSocialProfileId",
                schema: "v3",
                table: "CreatorApplications");

            migrationBuilder.DropColumn(
                name: "Platform",
                schema: "v3",
                table: "CreatorApplications");

            migrationBuilder.DropColumn(
                name: "CreatorSocialProfileId",
                schema: "v3",
                table: "CreatorAllocations");

            migrationBuilder.DropColumn(
                name: "Platform",
                schema: "v3",
                table: "CreatorAllocations");

            migrationBuilder.DropColumn(
                name: "UgcOpportunityId",
                schema: "v3",
                table: "AuditEvents");
        }
    }
}
