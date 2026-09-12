using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Weymela.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddViewRewardsQrAndPayouts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsAnomaly",
                schema: "v3",
                table: "PromotionViewVerifications",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsBaseline",
                schema: "v3",
                table: "PromotionViewVerifications",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<Guid>(
                name: "ParticipationId",
                schema: "v3",
                table: "PromotionViewVerifications",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ReportedViews",
                schema: "v3",
                table: "PromotionViewVerifications",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SettledBy",
                schema: "v3",
                table: "PlatformSettlements",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CommercePermissions",
                schema: "v3",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Role = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SubjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CanCheckout = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommercePermissions", x => new { x.UserId, x.Role, x.SubjectId });
                });

            migrationBuilder.CreateTable(
                name: "CreatorPromotionParticipations",
                schema: "v3",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PromotionId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatorAllocationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "text", nullable: false),
                    ExternalContentId = table.Column<string>(type: "text", nullable: false),
                    BaselineViews = table.Column<long>(type: "bigint", nullable: false),
                    LatestVerifiedViews = table.Column<long>(type: "bigint", nullable: false),
                    RewardedViewCount = table.Column<long>(type: "bigint", nullable: false),
                    Status = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    WentLiveAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LatestVerifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CreatorPromotionParticipations", x => x.Id);
                    table.CheckConstraint("CK_Participation_Views", "\"BaselineViews\" >= 0 AND \"LatestVerifiedViews\" >= \"BaselineViews\" AND \"RewardedViewCount\" >= 0 AND \"RewardedViewCount\" <= \"LatestVerifiedViews\" - \"BaselineViews\"");
                    table.ForeignKey(
                        name: "FK_CreatorPromotionParticipations_CreatorAllocations_CreatorAl~",
                        columns: x => new { x.CreatorAllocationId, x.PromotionId, x.CreatorId },
                        principalSchema: "v3",
                        principalTable: "CreatorAllocations",
                        principalColumns: new[] { "Id", "PromotionId", "CreatorId" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "OfferQrSessions",
                schema: "v3",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    PromotionId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatorAllocationId = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uuid", nullable: false),
                    TokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    IssuedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UsedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SaleId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    IdempotencyReference = table.Column<string>(type: "text", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OfferQrSessions", x => x.Id);
                    table.CheckConstraint("CK_Qr_Expiry", "\"ExpiresAtUtc\" = \"IssuedAtUtc\" + INTERVAL '5 minutes'");
                    table.ForeignKey(
                        name: "FK_OfferQrSessions_CreatorAllocations_CreatorAllocationId_Prom~",
                        columns: x => new { x.CreatorAllocationId, x.PromotionId, x.CreatorId },
                        principalSchema: "v3",
                        principalTable: "CreatorAllocations",
                        principalColumns: new[] { "Id", "PromotionId", "CreatorId" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_OfferQrSessions_VerifiedSales_SaleId",
                        column: x => x.SaleId,
                        principalSchema: "v3",
                        principalTable: "VerifiedSales",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PayoutRecords",
                schema: "v3",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Beneficiary = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: true),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: true),
                    ThresholdUsed = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ConfigurationVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    EligibleAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    PaidAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PaidBy = table.Column<Guid>(type: "uuid", nullable: true),
                    Reference = table.Column<string>(type: "text", nullable: true),
                    JournalId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PayoutRecords", x => x.Id);
                    table.CheckConstraint("CK_Payout_Amounts", "\"Amount\" > 0 AND \"Amount\" = \"ThresholdUsed\" AND ((\"Beneficiary\"='Creator' AND \"CreatorId\" IS NOT NULL AND \"CustomerId\" IS NULL) OR (\"Beneficiary\"='Customer' AND \"CustomerId\" IS NOT NULL AND \"CreatorId\" IS NULL))");
                    table.ForeignKey(
                        name: "FK_PayoutRecords_CreatorEarningsAccounts_CreatorId",
                        column: x => x.CreatorId,
                        principalSchema: "v3",
                        principalTable: "CreatorEarningsAccounts",
                        principalColumn: "CreatorId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PayoutRecords_CustomerCashbackAccounts_CustomerId",
                        column: x => x.CustomerId,
                        principalSchema: "v3",
                        principalTable: "CustomerCashbackAccounts",
                        principalColumn: "CustomerId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PayoutRecords_FinancialConfigurationVersions_ConfigurationV~",
                        column: x => x.ConfigurationVersionId,
                        principalSchema: "v3",
                        principalTable: "FinancialConfigurationVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PayoutRecords_FinancialJournals_JournalId",
                        column: x => x.JournalId,
                        principalSchema: "v3",
                        principalTable: "FinancialJournals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ViewRewardReceipts",
                schema: "v3",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ParticipationId = table.Column<Guid>(type: "uuid", nullable: false),
                    JournalId = table.Column<Guid>(type: "uuid", nullable: false),
                    Blocks = table.Column<long>(type: "bigint", nullable: false),
                    RewardedThrough = table.Column<long>(type: "bigint", nullable: false),
                    BusinessCharge = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    CreatorEarning = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    PlatformEarning = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ViewRewardReceipts", x => x.Id);
                    table.CheckConstraint("CK_ViewReward_Amounts", "\"Blocks\" > 0 AND \"RewardedThrough\" > 0 AND \"BusinessCharge\" > 0 AND \"BusinessCharge\" = \"CreatorEarning\" + \"PlatformEarning\"");
                    table.ForeignKey(
                        name: "FK_ViewRewardReceipts_CreatorPromotionParticipations_Participa~",
                        column: x => x.ParticipationId,
                        principalSchema: "v3",
                        principalTable: "CreatorPromotionParticipations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ViewRewardReceipts_FinancialJournals_JournalId",
                        column: x => x.JournalId,
                        principalSchema: "v3",
                        principalTable: "FinancialJournals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PromotionViewVerifications_ParticipationId",
                schema: "v3",
                table: "PromotionViewVerifications",
                column: "ParticipationId");

            migrationBuilder.CreateIndex(
                name: "IX_CommercePermissions_SubjectId_Role_IsActive",
                schema: "v3",
                table: "CommercePermissions",
                columns: new[] { "SubjectId", "Role", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_CreatorPromotionParticipations_CreatorAllocationId",
                schema: "v3",
                table: "CreatorPromotionParticipations",
                column: "CreatorAllocationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CreatorPromotionParticipations_CreatorAllocationId_Promotio~",
                schema: "v3",
                table: "CreatorPromotionParticipations",
                columns: new[] { "CreatorAllocationId", "PromotionId", "CreatorId" });

            migrationBuilder.CreateIndex(
                name: "IX_CreatorPromotionParticipations_Provider_ExternalContentId",
                schema: "v3",
                table: "CreatorPromotionParticipations",
                columns: new[] { "Provider", "ExternalContentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OfferQrSessions_CreatorAllocationId_PromotionId_CreatorId",
                schema: "v3",
                table: "OfferQrSessions",
                columns: new[] { "CreatorAllocationId", "PromotionId", "CreatorId" });

            migrationBuilder.CreateIndex(
                name: "IX_OfferQrSessions_CustomerId_IssuedAtUtc",
                schema: "v3",
                table: "OfferQrSessions",
                columns: new[] { "CustomerId", "IssuedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_OfferQrSessions_SaleId",
                schema: "v3",
                table: "OfferQrSessions",
                column: "SaleId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OfferQrSessions_TokenHash",
                schema: "v3",
                table: "OfferQrSessions",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PayoutRecords_ConfigurationVersionId",
                schema: "v3",
                table: "PayoutRecords",
                column: "ConfigurationVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_PayoutRecords_CreatorId",
                schema: "v3",
                table: "PayoutRecords",
                column: "CreatorId",
                unique: true,
                filter: "\"Status\"='Eligible' AND \"CreatorId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PayoutRecords_CustomerId",
                schema: "v3",
                table: "PayoutRecords",
                column: "CustomerId",
                unique: true,
                filter: "\"Status\"='Eligible' AND \"CustomerId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PayoutRecords_JournalId",
                schema: "v3",
                table: "PayoutRecords",
                column: "JournalId");

            migrationBuilder.CreateIndex(
                name: "IX_PayoutRecords_Reference",
                schema: "v3",
                table: "PayoutRecords",
                column: "Reference",
                unique: true,
                filter: "\"Reference\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_ViewRewardReceipts_JournalId",
                schema: "v3",
                table: "ViewRewardReceipts",
                column: "JournalId");

            migrationBuilder.CreateIndex(
                name: "IX_ViewRewardReceipts_ParticipationId_RewardedThrough",
                schema: "v3",
                table: "ViewRewardReceipts",
                columns: new[] { "ParticipationId", "RewardedThrough" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_PromotionViewVerifications_CreatorPromotionParticipations_P~",
                schema: "v3",
                table: "PromotionViewVerifications",
                column: "ParticipationId",
                principalSchema: "v3",
                principalTable: "CreatorPromotionParticipations",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
            migrationBuilder.Sql(Phase4IntegritySql.Create);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException("Financial history migrations are forward-only. Use a reviewed compensating migration; never drop posted financial history.");
        }
    }
}
