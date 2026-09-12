using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Weymela.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialV3Schema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "v3");

            migrationBuilder.CreateTable(
                name: "AuditEvents",
                schema: "v3",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EventType = table.Column<string>(type: "text", nullable: false),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uuid", nullable: true),
                    PromotionId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true),
                    CorrelationId = table.Column<Guid>(type: "uuid", nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Detail = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "BusinessWallets",
                schema: "v3",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uuid", nullable: false),
                    AvailableBalance = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ReservedBalance = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    TotalBalance = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false, computedColumnSql: "\"AvailableBalance\" + \"ReservedBalance\"", stored: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BusinessWallets", x => x.Id);
                    table.UniqueConstraint("AK_BusinessWallets_BusinessId", x => x.BusinessId);
                    table.CheckConstraint("CK_Wallet_NonNegative", "\"AvailableBalance\" >= 0 AND \"ReservedBalance\" >= 0");
                });

            migrationBuilder.CreateTable(
                name: "CreatorEarningsAccounts",
                schema: "v3",
                columns: table => new
                {
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: false),
                    AvailableEarnings = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CreatorEarningsAccounts", x => x.CreatorId);
                    table.CheckConstraint("CK_CreatorEarnings_NonNegative", "\"AvailableEarnings\" >= 0");
                });

            migrationBuilder.CreateTable(
                name: "CustomerCashbackAccounts",
                schema: "v3",
                columns: table => new
                {
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    AvailableCashback = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerCashbackAccounts", x => x.CustomerId);
                    table.CheckConstraint("CK_Cashback_NonNegative", "\"AvailableCashback\" >= 0");
                });

            migrationBuilder.CreateTable(
                name: "FinancialConfigurations",
                schema: "v3",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinancialConfigurations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IdempotencyRecords",
                schema: "v3",
                columns: table => new
                {
                    ActorId = table.Column<Guid>(type: "uuid", nullable: false),
                    OperationType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    RequestFingerprint = table.Column<string>(type: "text", nullable: false),
                    ResultReference = table.Column<string>(type: "text", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IdempotencyRecords", x => new { x.ActorId, x.OperationType, x.Key });
                });

            migrationBuilder.CreateTable(
                name: "LegalDocumentVersions",
                schema: "v3",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Version = table.Column<string>(type: "text", nullable: false),
                    ContentHash = table.Column<string>(type: "text", nullable: false),
                    EffectiveFromUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LegalDocumentVersions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OutboxMessages",
                schema: "v3",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    EventType = table.Column<string>(type: "text", nullable: false),
                    Payload = table.Column<string>(type: "jsonb", nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ProcessedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    LastError = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboxMessages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Promotions",
                schema: "v3",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PublicPromotionId = table.Column<string>(type: "text", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uuid", nullable: false),
                    Title = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    PromotionType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TotalBudget = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ReservedBudget = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    UsedBudget = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Eligibility_Category = table.Column<string>(type: "text", nullable: true),
                    Eligibility_MinimumVerifiedFollowers = table.Column<long>(type: "bigint", nullable: true),
                    Eligibility_Market = table.Column<string>(type: "text", nullable: true),
                    Eligibility_Requirements = table.Column<string>(type: "text", nullable: true),
                    StartDateUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndDateUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PublishedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ActivatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    AllocatedBudget = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    RemainingBudget = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false, computedColumnSql: "\"TotalBudget\" - \"UsedBudget\"", stored: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Promotions", x => x.Id);
                    table.CheckConstraint("CK_Promotion_Budgets", "\"TotalBudget\" > 0 AND \"ReservedBudget\" >= 0 AND \"UsedBudget\" >= 0 AND \"ReservedBudget\" + \"UsedBudget\" <= \"TotalBudget\" AND \"AllocatedBudget\" >= \"UsedBudget\" AND \"AllocatedBudget\" <= \"TotalBudget\"");
                    table.CheckConstraint("CK_Promotion_Dates", "\"EndDateUtc\" > \"StartDateUtc\"");
                    table.ForeignKey(
                        name: "FK_Promotions_BusinessWallets_BusinessId",
                        column: x => x.BusinessId,
                        principalSchema: "v3",
                        principalTable: "BusinessWallets",
                        principalColumn: "BusinessId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FinancialConfigurationVersions",
                schema: "v3",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ConfigurationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    ChangedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    EffectiveFromUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ViewOnly_PromotionType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ViewOnly_ViewsPerReward = table.Column<int>(type: "integer", nullable: false),
                    ViewOnly_BusinessCharge = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ViewOnly_CreatorEarning = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ViewOnly_PlatformEarning = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ViewOnly_CreatorCommissionPercent = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: false),
                    ViewOnly_CustomerCashbackPercent = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: false),
                    ViewOnly_PlatformPercent = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: false),
                    ViewOnly_EffectiveFromUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ViewOnly_ConfigurationVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ViewOnly_MinimumPromotionBudget = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    ViewPlusCommission_PromotionType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ViewPlusCommission_ViewsPerReward = table.Column<int>(type: "integer", nullable: false),
                    ViewPlusCommission_BusinessCharge = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ViewPlusCommission_CreatorEarning = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ViewPlusCommission_PlatformEarning = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ViewPlusCommission_CreatorCommissionPercent = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: false),
                    ViewPlusCommission_CustomerCashbackPercent = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: false),
                    ViewPlusCommission_PlatformPercent = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: false),
                    ViewPlusCommission_EffectiveFromUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ViewPlusCommission_ConfigurationVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ViewPlusCommission_MinimumPromotionBudget = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true),
                    CreatorPayoutThreshold = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    CustomerPayoutThreshold = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinancialConfigurationVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FinancialConfigurationVersions_FinancialConfigurations_Conf~",
                        column: x => x.ConfigurationId,
                        principalSchema: "v3",
                        principalTable: "FinancialConfigurations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LegalAcceptances",
                schema: "v3",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Role = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DocumentVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    AcceptedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IpReference = table.Column<string>(type: "text", nullable: true),
                    UserAgentReference = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LegalAcceptances", x => new { x.UserId, x.Role, x.DocumentVersionId });
                    table.ForeignKey(
                        name: "FK_LegalAcceptances_LegalDocumentVersions_DocumentVersionId",
                        column: x => x.DocumentVersionId,
                        principalSchema: "v3",
                        principalTable: "LegalDocumentVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CreatorAllocations",
                schema: "v3",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    PromotionId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: false),
                    OriginalAllocation = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    UsedAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Status = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ApprovedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ActivatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RemainingAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false, computedColumnSql: "\"OriginalAllocation\" - \"UsedAmount\"", stored: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CreatorAllocations", x => x.Id);
                    table.UniqueConstraint("AK_CreatorAllocations_Id_PromotionId_CreatorId", x => new { x.Id, x.PromotionId, x.CreatorId });
                    table.CheckConstraint("CK_Allocation_Budgets", "\"OriginalAllocation\" > 0 AND \"UsedAmount\" >= 0 AND \"UsedAmount\" <= \"OriginalAllocation\"");
                    table.ForeignKey(
                        name: "FK_CreatorAllocations_Promotions_PromotionId",
                        column: x => x.PromotionId,
                        principalSchema: "v3",
                        principalTable: "Promotions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CreatorApplications",
                schema: "v3",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PromotionId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: false),
                    Message = table.Column<string>(type: "text", nullable: false),
                    ContentConcept = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AppliedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ReviewedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReviewedByBusinessUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CreatorApplications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CreatorApplications_Promotions_PromotionId",
                        column: x => x.PromotionId,
                        principalSchema: "v3",
                        principalTable: "Promotions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FinancialJournals",
                schema: "v3",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Reference = table.Column<string>(type: "text", nullable: false),
                    CorrelationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SourceType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    IdempotencyReference = table.Column<string>(type: "text", nullable: true),
                    IsPosted = table.Column<bool>(type: "boolean", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: true),
                    PromotionId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinancialJournals", x => x.Id);
                    table.CheckConstraint("CK_Journal_Posted", "\"IsPosted\"");
                    table.ForeignKey(
                        name: "FK_FinancialJournals_Promotions_PromotionId",
                        column: x => x.PromotionId,
                        principalSchema: "v3",
                        principalTable: "Promotions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PricingSnapshots",
                schema: "v3",
                columns: table => new
                {
                    PromotionId = table.Column<Guid>(type: "uuid", nullable: false),
                    PromotionType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ViewsPerReward = table.Column<int>(type: "integer", nullable: false),
                    BusinessCharge = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    CreatorEarning = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    PlatformEarning = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    CreatorCommissionPercent = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: false),
                    CustomerCashbackPercent = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: false),
                    PlatformPercent = table.Column<decimal>(type: "numeric(9,4)", precision: 9, scale: 4, nullable: false),
                    EffectiveFromUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ConfigurationVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    MinimumPromotionBudget = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PricingSnapshots", x => x.PromotionId);
                    table.ForeignKey(
                        name: "FK_PricingSnapshots_Promotions_PromotionId",
                        column: x => x.PromotionId,
                        principalSchema: "v3",
                        principalTable: "Promotions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PromotionViewVerifications",
                schema: "v3",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PromotionId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatorAllocationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExternalPlatform = table.Column<string>(type: "text", nullable: false),
                    ExternalContentId = table.Column<string>(type: "text", nullable: false),
                    PreviousVerifiedViews = table.Column<long>(type: "bigint", nullable: false),
                    CurrentVerifiedViews = table.Column<long>(type: "bigint", nullable: false),
                    RewardedViewCount = table.Column<long>(type: "bigint", nullable: false),
                    VerifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EvidenceReference = table.Column<string>(type: "text", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PromotionViewVerifications", x => x.Id);
                    table.CheckConstraint("CK_Views_Valid", "\"PreviousVerifiedViews\" >= 0 AND \"CurrentVerifiedViews\" >= \"PreviousVerifiedViews\" AND \"RewardedViewCount\" >= 0");
                    table.ForeignKey(
                        name: "FK_PromotionViewVerifications_CreatorAllocations_CreatorAlloca~",
                        columns: x => new { x.CreatorAllocationId, x.PromotionId, x.CreatorId },
                        principalSchema: "v3",
                        principalTable: "CreatorAllocations",
                        principalColumns: new[] { "Id", "PromotionId", "CreatorId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CreatorEarningEntries",
                schema: "v3",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: false),
                    PromotionId = table.Column<Guid>(type: "uuid", nullable: true),
                    Source = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CorrelationId = table.Column<Guid>(type: "uuid", nullable: false),
                    JournalId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CreatorEarningEntries", x => x.Id);
                    table.CheckConstraint("CK_CreatorEarning_Positive", "\"Amount\" > 0");
                    table.ForeignKey(
                        name: "FK_CreatorEarningEntries_CreatorEarningsAccounts_CreatorId",
                        column: x => x.CreatorId,
                        principalSchema: "v3",
                        principalTable: "CreatorEarningsAccounts",
                        principalColumn: "CreatorId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CreatorEarningEntries_FinancialJournals_JournalId",
                        column: x => x.JournalId,
                        principalSchema: "v3",
                        principalTable: "FinancialJournals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CreatorEarningEntries_Promotions_PromotionId",
                        column: x => x.PromotionId,
                        principalSchema: "v3",
                        principalTable: "Promotions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "FinancialJournalLines",
                schema: "v3",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Account = table.Column<string>(type: "text", nullable: false),
                    JournalId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinancialJournalLines", x => x.Id);
                    table.CheckConstraint("CK_JournalLine_Positive", "\"Amount\" > 0 AND \"Type\" IN ('Debit', 'Credit')");
                    table.ForeignKey(
                        name: "FK_FinancialJournalLines_FinancialJournals_JournalId",
                        column: x => x.JournalId,
                        principalSchema: "v3",
                        principalTable: "FinancialJournals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PlatformRevenueEntries",
                schema: "v3",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PromotionId = table.Column<Guid>(type: "uuid", nullable: true),
                    Source = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Status = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CorrelationId = table.Column<Guid>(type: "uuid", nullable: false),
                    JournalId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlatformRevenueEntries", x => x.Id);
                    table.CheckConstraint("CK_Revenue_Positive", "\"Amount\" > 0");
                    table.ForeignKey(
                        name: "FK_PlatformRevenueEntries_FinancialJournals_JournalId",
                        column: x => x.JournalId,
                        principalSchema: "v3",
                        principalTable: "FinancialJournals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PlatformRevenueEntries_Promotions_PromotionId",
                        column: x => x.PromotionId,
                        principalSchema: "v3",
                        principalTable: "Promotions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PlatformSettlements",
                schema: "v3",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    SettledAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Reference = table.Column<string>(type: "text", nullable: false),
                    JournalId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlatformSettlements", x => x.Id);
                    table.CheckConstraint("CK_Settlement_Positive", "\"Amount\" > 0");
                    table.ForeignKey(
                        name: "FK_PlatformSettlements_FinancialJournals_JournalId",
                        column: x => x.JournalId,
                        principalSchema: "v3",
                        principalTable: "FinancialJournals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PromotionBudgetEntries",
                schema: "v3",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PromotionId = table.Column<Guid>(type: "uuid", nullable: false),
                    AllocationId = table.Column<Guid>(type: "uuid", nullable: true),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Movement = table.Column<string>(type: "text", nullable: false),
                    JournalId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PromotionBudgetEntries", x => x.Id);
                    table.CheckConstraint("CK_BudgetEntry_Positive", "\"Amount\" > 0");
                    table.ForeignKey(
                        name: "FK_PromotionBudgetEntries_CreatorAllocations_AllocationId",
                        column: x => x.AllocationId,
                        principalSchema: "v3",
                        principalTable: "CreatorAllocations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PromotionBudgetEntries_FinancialJournals_JournalId",
                        column: x => x.JournalId,
                        principalSchema: "v3",
                        principalTable: "FinancialJournals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PromotionBudgetEntries_Promotions_PromotionId",
                        column: x => x.PromotionId,
                        principalSchema: "v3",
                        principalTable: "Promotions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PromotionReservations",
                schema: "v3",
                columns: table => new
                {
                    PromotionId = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uuid", nullable: false),
                    OriginalAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    JournalId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PromotionReservations", x => x.PromotionId);
                    table.CheckConstraint("CK_Reservation_Positive", "\"OriginalAmount\" > 0");
                    table.ForeignKey(
                        name: "FK_PromotionReservations_BusinessWallets_BusinessId",
                        column: x => x.BusinessId,
                        principalSchema: "v3",
                        principalTable: "BusinessWallets",
                        principalColumn: "BusinessId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PromotionReservations_FinancialJournals_JournalId",
                        column: x => x.JournalId,
                        principalSchema: "v3",
                        principalTable: "FinancialJournals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PromotionReservations_Promotions_PromotionId",
                        column: x => x.PromotionId,
                        principalSchema: "v3",
                        principalTable: "Promotions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "VerifiedSales",
                schema: "v3",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PromotionId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatorAllocationId = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    CashierId = table.Column<Guid>(type: "uuid", nullable: false),
                    PurchaseAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    CreatorCommissionAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    CustomerCashbackAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    PlatformRevenueAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    TotalPromotionCharge = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    QrTokenReference = table.Column<string>(type: "text", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "text", nullable: false),
                    JournalId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VerifiedSales", x => x.Id);
                    table.CheckConstraint("CK_Sale_Amounts", "\"PurchaseAmount\" > 0 AND \"CreatorCommissionAmount\" >= 0 AND \"CustomerCashbackAmount\" >= 0 AND \"PlatformRevenueAmount\" >= 0 AND \"TotalPromotionCharge\" = \"CreatorCommissionAmount\" + \"CustomerCashbackAmount\" + \"PlatformRevenueAmount\"");
                    table.ForeignKey(
                        name: "FK_VerifiedSales_CreatorAllocations_CreatorAllocationId_Promot~",
                        columns: x => new { x.CreatorAllocationId, x.PromotionId, x.CreatorId },
                        principalSchema: "v3",
                        principalTable: "CreatorAllocations",
                        principalColumns: new[] { "Id", "PromotionId", "CreatorId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_VerifiedSales_FinancialJournals_JournalId",
                        column: x => x.JournalId,
                        principalSchema: "v3",
                        principalTable: "FinancialJournals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WalletEntries",
                schema: "v3",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uuid", nullable: false),
                    PromotionId = table.Column<Guid>(type: "uuid", nullable: true),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Movement = table.Column<string>(type: "text", nullable: false),
                    JournalId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WalletEntries", x => x.Id);
                    table.CheckConstraint("CK_WalletEntry_Positive", "\"Amount\" > 0");
                    table.ForeignKey(
                        name: "FK_WalletEntries_BusinessWallets_BusinessId",
                        column: x => x.BusinessId,
                        principalSchema: "v3",
                        principalTable: "BusinessWallets",
                        principalColumn: "BusinessId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WalletEntries_FinancialJournals_JournalId",
                        column: x => x.JournalId,
                        principalSchema: "v3",
                        principalTable: "FinancialJournals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CustomerCashbackEntries",
                schema: "v3",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    VerifiedSaleId = table.Column<Guid>(type: "uuid", nullable: true),
                    Source = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CorrelationId = table.Column<Guid>(type: "uuid", nullable: false),
                    JournalId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomerCashbackEntries", x => x.Id);
                    table.CheckConstraint("CK_Cashback_PositiveSale", "\"Amount\" > 0 AND (\"Source\" <> 'VerifiedSale' OR \"VerifiedSaleId\" IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_CustomerCashbackEntries_CustomerCashbackAccounts_CustomerId",
                        column: x => x.CustomerId,
                        principalSchema: "v3",
                        principalTable: "CustomerCashbackAccounts",
                        principalColumn: "CustomerId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CustomerCashbackEntries_FinancialJournals_JournalId",
                        column: x => x.JournalId,
                        principalSchema: "v3",
                        principalTable: "FinancialJournals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CustomerCashbackEntries_VerifiedSales_VerifiedSaleId",
                        column: x => x.VerifiedSaleId,
                        principalSchema: "v3",
                        principalTable: "VerifiedSales",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_CorrelationId",
                schema: "v3",
                table: "AuditEvents",
                column: "CorrelationId");

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_PromotionId_OccurredAtUtc",
                schema: "v3",
                table: "AuditEvents",
                columns: new[] { "PromotionId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_CreatorAllocations_PromotionId_CreatorId",
                schema: "v3",
                table: "CreatorAllocations",
                columns: new[] { "PromotionId", "CreatorId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CreatorApplications_PromotionId_CreatorId",
                schema: "v3",
                table: "CreatorApplications",
                columns: new[] { "PromotionId", "CreatorId" },
                unique: true,
                filter: "\"Status\" IN ('Pending', 'Approved')");

            migrationBuilder.CreateIndex(
                name: "IX_CreatorApplications_PromotionId_Status",
                schema: "v3",
                table: "CreatorApplications",
                columns: new[] { "PromotionId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_CreatorEarningEntries_CreatorId",
                schema: "v3",
                table: "CreatorEarningEntries",
                column: "CreatorId");

            migrationBuilder.CreateIndex(
                name: "IX_CreatorEarningEntries_JournalId",
                schema: "v3",
                table: "CreatorEarningEntries",
                column: "JournalId");

            migrationBuilder.CreateIndex(
                name: "IX_CreatorEarningEntries_PromotionId",
                schema: "v3",
                table: "CreatorEarningEntries",
                column: "PromotionId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerCashbackEntries_CustomerId",
                schema: "v3",
                table: "CustomerCashbackEntries",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerCashbackEntries_JournalId",
                schema: "v3",
                table: "CustomerCashbackEntries",
                column: "JournalId");

            migrationBuilder.CreateIndex(
                name: "IX_CustomerCashbackEntries_VerifiedSaleId",
                schema: "v3",
                table: "CustomerCashbackEntries",
                column: "VerifiedSaleId");

            migrationBuilder.CreateIndex(
                name: "IX_FinancialConfigurations_Name",
                schema: "v3",
                table: "FinancialConfigurations",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FinancialConfigurationVersions_ConfigurationId_Version",
                schema: "v3",
                table: "FinancialConfigurationVersions",
                columns: new[] { "ConfigurationId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FinancialConfigurationVersions_EffectiveFromUtc_Version",
                schema: "v3",
                table: "FinancialConfigurationVersions",
                columns: new[] { "EffectiveFromUtc", "Version" });

            migrationBuilder.CreateIndex(
                name: "IX_FinancialJournalLines_Account",
                schema: "v3",
                table: "FinancialJournalLines",
                column: "Account");

            migrationBuilder.CreateIndex(
                name: "IX_FinancialJournalLines_JournalId",
                schema: "v3",
                table: "FinancialJournalLines",
                column: "JournalId");

            migrationBuilder.CreateIndex(
                name: "IX_FinancialJournals_BusinessId",
                schema: "v3",
                table: "FinancialJournals",
                column: "BusinessId");

            migrationBuilder.CreateIndex(
                name: "IX_FinancialJournals_CorrelationId",
                schema: "v3",
                table: "FinancialJournals",
                column: "CorrelationId");

            migrationBuilder.CreateIndex(
                name: "IX_FinancialJournals_CreatedAtUtc",
                schema: "v3",
                table: "FinancialJournals",
                column: "CreatedAtUtc");

            migrationBuilder.CreateIndex(
                name: "IX_FinancialJournals_CreatorId",
                schema: "v3",
                table: "FinancialJournals",
                column: "CreatorId");

            migrationBuilder.CreateIndex(
                name: "IX_FinancialJournals_CustomerId",
                schema: "v3",
                table: "FinancialJournals",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_FinancialJournals_IdempotencyReference",
                schema: "v3",
                table: "FinancialJournals",
                column: "IdempotencyReference");

            migrationBuilder.CreateIndex(
                name: "IX_FinancialJournals_PromotionId",
                schema: "v3",
                table: "FinancialJournals",
                column: "PromotionId");

            migrationBuilder.CreateIndex(
                name: "IX_FinancialJournals_Reference",
                schema: "v3",
                table: "FinancialJournals",
                column: "Reference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FinancialJournals_SourceType",
                schema: "v3",
                table: "FinancialJournals",
                column: "SourceType");

            migrationBuilder.CreateIndex(
                name: "IX_LegalAcceptances_DocumentVersionId",
                schema: "v3",
                table: "LegalAcceptances",
                column: "DocumentVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_LegalDocumentVersions_Type_EffectiveFromUtc",
                schema: "v3",
                table: "LegalDocumentVersions",
                columns: new[] { "Type", "EffectiveFromUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_LegalDocumentVersions_Type_Version",
                schema: "v3",
                table: "LegalDocumentVersions",
                columns: new[] { "Type", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_OccurredAtUtc",
                schema: "v3",
                table: "OutboxMessages",
                column: "OccurredAtUtc",
                filter: "\"ProcessedAtUtc\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PlatformRevenueEntries_JournalId",
                schema: "v3",
                table: "PlatformRevenueEntries",
                column: "JournalId");

            migrationBuilder.CreateIndex(
                name: "IX_PlatformRevenueEntries_PromotionId",
                schema: "v3",
                table: "PlatformRevenueEntries",
                column: "PromotionId");

            migrationBuilder.CreateIndex(
                name: "IX_PlatformRevenueEntries_Source_CreatedAtUtc",
                schema: "v3",
                table: "PlatformRevenueEntries",
                columns: new[] { "Source", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_PlatformSettlements_JournalId",
                schema: "v3",
                table: "PlatformSettlements",
                column: "JournalId");

            migrationBuilder.CreateIndex(
                name: "IX_PlatformSettlements_Reference",
                schema: "v3",
                table: "PlatformSettlements",
                column: "Reference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PromotionBudgetEntries_AllocationId",
                schema: "v3",
                table: "PromotionBudgetEntries",
                column: "AllocationId");

            migrationBuilder.CreateIndex(
                name: "IX_PromotionBudgetEntries_JournalId",
                schema: "v3",
                table: "PromotionBudgetEntries",
                column: "JournalId");

            migrationBuilder.CreateIndex(
                name: "IX_PromotionBudgetEntries_PromotionId_CreatedAtUtc",
                schema: "v3",
                table: "PromotionBudgetEntries",
                columns: new[] { "PromotionId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_PromotionReservations_BusinessId",
                schema: "v3",
                table: "PromotionReservations",
                column: "BusinessId");

            migrationBuilder.CreateIndex(
                name: "IX_PromotionReservations_JournalId",
                schema: "v3",
                table: "PromotionReservations",
                column: "JournalId");

            migrationBuilder.CreateIndex(
                name: "IX_Promotions_BusinessId_Status",
                schema: "v3",
                table: "Promotions",
                columns: new[] { "BusinessId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Promotions_PublicPromotionId",
                schema: "v3",
                table: "Promotions",
                column: "PublicPromotionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PromotionViewVerifications_CreatorAllocationId_PromotionId_~",
                schema: "v3",
                table: "PromotionViewVerifications",
                columns: new[] { "CreatorAllocationId", "PromotionId", "CreatorId" });

            migrationBuilder.CreateIndex(
                name: "IX_PromotionViewVerifications_IdempotencyKey",
                schema: "v3",
                table: "PromotionViewVerifications",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VerifiedSales_BusinessId_IdempotencyKey",
                schema: "v3",
                table: "VerifiedSales",
                columns: new[] { "BusinessId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VerifiedSales_CreatorAllocationId_PromotionId_CreatorId",
                schema: "v3",
                table: "VerifiedSales",
                columns: new[] { "CreatorAllocationId", "PromotionId", "CreatorId" });

            migrationBuilder.CreateIndex(
                name: "IX_VerifiedSales_JournalId",
                schema: "v3",
                table: "VerifiedSales",
                column: "JournalId");

            migrationBuilder.CreateIndex(
                name: "IX_WalletEntries_BusinessId_CreatedAtUtc",
                schema: "v3",
                table: "WalletEntries",
                columns: new[] { "BusinessId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_WalletEntries_JournalId",
                schema: "v3",
                table: "WalletEntries",
                column: "JournalId");
            migrationBuilder.Sql(IntegritySql.Create);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditEvents",
                schema: "v3");

            migrationBuilder.DropTable(
                name: "CreatorApplications",
                schema: "v3");

            migrationBuilder.DropTable(
                name: "CreatorEarningEntries",
                schema: "v3");

            migrationBuilder.DropTable(
                name: "CustomerCashbackEntries",
                schema: "v3");

            migrationBuilder.DropTable(
                name: "FinancialConfigurationVersions",
                schema: "v3");

            migrationBuilder.DropTable(
                name: "FinancialJournalLines",
                schema: "v3");

            migrationBuilder.DropTable(
                name: "IdempotencyRecords",
                schema: "v3");

            migrationBuilder.DropTable(
                name: "LegalAcceptances",
                schema: "v3");

            migrationBuilder.DropTable(
                name: "OutboxMessages",
                schema: "v3");

            migrationBuilder.DropTable(
                name: "PlatformRevenueEntries",
                schema: "v3");

            migrationBuilder.DropTable(
                name: "PlatformSettlements",
                schema: "v3");

            migrationBuilder.DropTable(
                name: "PricingSnapshots",
                schema: "v3");

            migrationBuilder.DropTable(
                name: "PromotionBudgetEntries",
                schema: "v3");

            migrationBuilder.DropTable(
                name: "PromotionReservations",
                schema: "v3");

            migrationBuilder.DropTable(
                name: "PromotionViewVerifications",
                schema: "v3");

            migrationBuilder.DropTable(
                name: "WalletEntries",
                schema: "v3");

            migrationBuilder.DropTable(
                name: "CreatorEarningsAccounts",
                schema: "v3");

            migrationBuilder.DropTable(
                name: "CustomerCashbackAccounts",
                schema: "v3");

            migrationBuilder.DropTable(
                name: "VerifiedSales",
                schema: "v3");

            migrationBuilder.DropTable(
                name: "FinancialConfigurations",
                schema: "v3");

            migrationBuilder.DropTable(
                name: "LegalDocumentVersions",
                schema: "v3");

            migrationBuilder.DropTable(
                name: "CreatorAllocations",
                schema: "v3");

            migrationBuilder.DropTable(
                name: "FinancialJournals",
                schema: "v3");

            migrationBuilder.DropTable(
                name: "Promotions",
                schema: "v3");

            migrationBuilder.DropTable(
                name: "BusinessWallets",
                schema: "v3");
        }
    }
}
