using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Weymela.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CompleteCreatorCollaborationWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_CreatorPromotionContentSubmission_Provider",
                schema: "v3",
                table: "CreatorPromotionContentSubmissions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CreatorPromotionContentSubmission_Reference",
                schema: "v3",
                table: "CreatorPromotionContentSubmissions");

            migrationBuilder.AlterColumn<string>(
                name: "SubmissionUrl",
                schema: "v3",
                table: "UgcSubmissions",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AddColumn<int>(
                name: "ContentRevisionNumber",
                schema: "v3",
                table: "UgcSubmissions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "ReviewMediaAssetId",
                schema: "v3",
                table: "UgcSubmissions",
                type: "uuid",
                nullable: true);

            migrationBuilder.Sql("""
                WITH ranked AS (
                    SELECT "Id", row_number() OVER (
                        PARTITION BY "UgcAssignmentId"
                        ORDER BY "SubmittedAtUtc", "Id") AS revision
                    FROM v3."UgcSubmissions"
                )
                UPDATE v3."UgcSubmissions" AS submission
                SET "ContentRevisionNumber" = ranked.revision
                FROM ranked
                WHERE submission."Id" = ranked."Id";
                """);

            migrationBuilder.AddColumn<Guid>(
                name: "ApprovedContentSubmissionId",
                schema: "v3",
                table: "CreatorPromotionParticipations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CreatorSocialProfileId",
                schema: "v3",
                table: "CreatorPromotionParticipations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Provider",
                schema: "v3",
                table: "CreatorPromotionContentSubmissions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64);

            migrationBuilder.AlterColumn<string>(
                name: "ContentReference",
                schema: "v3",
                table: "CreatorPromotionContentSubmissions",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100);

            migrationBuilder.AddColumn<Guid>(
                name: "ReviewMediaAssetId",
                schema: "v3",
                table: "CreatorPromotionContentSubmissions",
                type: "uuid",
                nullable: true);

            migrationBuilder.Sql("""
                UPDATE v3."CreatorPromotionParticipations" AS participation
                SET "CreatorSocialProfileId" = allocation."CreatorSocialProfileId"
                FROM v3."CreatorAllocations" AS allocation
                WHERE allocation."Id" = participation."CreatorAllocationId"
                  AND allocation."CreatorSocialProfileId" IS NOT NULL;

                UPDATE v3."CreatorPromotionParticipations" AS participation
                SET "ApprovedContentSubmissionId" = (
                    SELECT submission."Id"
                    FROM v3."CreatorPromotionContentSubmissions" AS submission
                    WHERE submission."CreatorAllocationId" = participation."CreatorAllocationId"
                      AND submission."ReviewStatus" = 'Approved'
                      AND submission."Provider" = participation."Provider"
                      AND submission."ContentReference" = participation."ExternalContentId"
                    ORDER BY submission."RevisionNumber" DESC
                    LIMIT 1
                )
                WHERE EXISTS (
                    SELECT 1
                    FROM v3."CreatorPromotionContentSubmissions" AS submission
                    WHERE submission."CreatorAllocationId" = participation."CreatorAllocationId"
                      AND submission."ReviewStatus" = 'Approved'
                      AND submission."Provider" = participation."Provider"
                      AND submission."ContentReference" = participation."ExternalContentId"
                );
                """);

            migrationBuilder.CreateTable(
                name: "CreatorPublicationVerifications",
                schema: "v3",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatorSocialProfileId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatorAllocationId = table.Column<Guid>(type: "uuid", nullable: true),
                    UgcAssignmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    PromotionContentSubmissionId = table.Column<Guid>(type: "uuid", nullable: true),
                    UgcSubmissionId = table.Column<Guid>(type: "uuid", nullable: true),
                    Provider = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ExternalContentId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    VerificationMethod = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    EvidenceReference = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    BaselineViews = table.Column<long>(type: "bigint", nullable: true),
                    RequestedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    VerifiedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    VerifiedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    WentLiveAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    EndedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CreatorPublicationVerifications", x => x.Id);
                    table.CheckConstraint("CK_Publication_Baseline", "\"BaselineViews\" IS NULL OR \"BaselineViews\" >= 0");
                    table.CheckConstraint("CK_Publication_Content", "length(trim(\"ExternalContentId\")) > 0 AND length(\"ExternalContentId\") <= 100");
                    table.CheckConstraint("CK_Publication_GoLive", "\"WentLiveAtUtc\" IS NULL OR \"VerifiedAtUtc\" IS NOT NULL");
                    table.CheckConstraint("CK_Publication_Provider", "\"Provider\" IN ('TikTok','YouTube','Instagram')");
                    table.CheckConstraint("CK_Publication_Status", "\"Status\" IN ('VerificationPending','Verified','Failed','Expired')");
                    table.CheckConstraint("CK_Publication_Verification", "(\"Status\" = 'Verified' AND \"VerifiedAtUtc\" IS NOT NULL AND \"EvidenceReference\" IS NOT NULL AND \"VerificationMethod\" IS NOT NULL) OR \"Status\" <> 'Verified'");
                    table.CheckConstraint("CK_Publication_Work", "(\"CreatorAllocationId\" IS NOT NULL AND \"UgcAssignmentId\" IS NULL AND \"PromotionContentSubmissionId\" IS NOT NULL AND \"UgcSubmissionId\" IS NULL) OR (\"CreatorAllocationId\" IS NULL AND \"UgcAssignmentId\" IS NOT NULL AND \"PromotionContentSubmissionId\" IS NULL AND \"UgcSubmissionId\" IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_CreatorPublicationVerifications_CreatorAllocations_CreatorA~",
                        column: x => x.CreatorAllocationId,
                        principalSchema: "v3",
                        principalTable: "CreatorAllocations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CreatorPublicationVerifications_CreatorPromotionContentSubm~",
                        column: x => x.PromotionContentSubmissionId,
                        principalSchema: "v3",
                        principalTable: "CreatorPromotionContentSubmissions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CreatorPublicationVerifications_CreatorSocialProfiles_Creat~",
                        column: x => x.CreatorSocialProfileId,
                        principalSchema: "v3",
                        principalTable: "CreatorSocialProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CreatorPublicationVerifications_UgcAssignments_UgcAssignmen~",
                        column: x => x.UgcAssignmentId,
                        principalSchema: "v3",
                        principalTable: "UgcAssignments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CreatorPublicationVerifications_UgcSubmissions_UgcSubmissio~",
                        column: x => x.UgcSubmissionId,
                        principalSchema: "v3",
                        principalTable: "UgcSubmissions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PrivateReviewMediaAssets",
                schema: "v3",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatorAllocationId = table.Column<Guid>(type: "uuid", nullable: true),
                    UgcAssignmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    RevisionNumber = table.Column<int>(type: "integer", nullable: false),
                    StorageKey = table.Column<string>(type: "character varying(66)", maxLength: 66, nullable: false),
                    ContentType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Length = table.Column<long>(type: "bigint", nullable: false),
                    Sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    OriginalFileName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    State = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PrivateReviewMediaAssets", x => x.Id);
                    table.CheckConstraint("CK_ReviewMedia_Digest", "\"Sha256\" ~ '^[0-9a-f]{64}$'");
                    table.CheckConstraint("CK_ReviewMedia_Key", "\"StorageKey\" ~ '^m_[0-9a-f]{64}$'");
                    table.CheckConstraint("CK_ReviewMedia_Length", "\"Length\" > 0");
                    table.CheckConstraint("CK_ReviewMedia_Revision", "\"RevisionNumber\" > 0");
                    table.CheckConstraint("CK_ReviewMedia_State", "\"State\" IN ('Active','Retained')");
                    table.CheckConstraint("CK_ReviewMedia_Type", "\"ContentType\" IN ('video/mp4','image/jpeg','image/png')");
                    table.CheckConstraint("CK_ReviewMedia_Work", "(\"CreatorAllocationId\" IS NOT NULL AND \"UgcAssignmentId\" IS NULL) OR (\"CreatorAllocationId\" IS NULL AND \"UgcAssignmentId\" IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_PrivateReviewMediaAssets_CreatorAllocations_CreatorAllocati~",
                        column: x => x.CreatorAllocationId,
                        principalSchema: "v3",
                        principalTable: "CreatorAllocations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PrivateReviewMediaAssets_UgcAssignments_UgcAssignmentId",
                        column: x => x.UgcAssignmentId,
                        principalSchema: "v3",
                        principalTable: "UgcAssignments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UgcSubmissions_ReviewMediaAssetId",
                schema: "v3",
                table: "UgcSubmissions",
                column: "ReviewMediaAssetId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UgcSubmissions_UgcAssignmentId_ContentRevisionNumber",
                schema: "v3",
                table: "UgcSubmissions",
                columns: new[] { "UgcAssignmentId", "ContentRevisionNumber" },
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_UgcSubmission_ContentRevision",
                schema: "v3",
                table: "UgcSubmissions",
                sql: "\"ContentRevisionNumber\" > 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_UgcSubmission_Source",
                schema: "v3",
                table: "UgcSubmissions",
                sql: "(\"ReviewMediaAssetId\" IS NOT NULL AND \"SubmissionUrl\" IS NULL) OR (\"ReviewMediaAssetId\" IS NULL AND length(trim(\"SubmissionUrl\")) > 0)");

            migrationBuilder.CreateIndex(
                name: "IX_CreatorPromotionParticipations_ApprovedContentSubmissionId",
                schema: "v3",
                table: "CreatorPromotionParticipations",
                column: "ApprovedContentSubmissionId");

            migrationBuilder.CreateIndex(
                name: "IX_CreatorPromotionParticipations_CreatorSocialProfileId",
                schema: "v3",
                table: "CreatorPromotionParticipations",
                column: "CreatorSocialProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_CreatorPromotionContentSubmissions_ReviewMediaAssetId",
                schema: "v3",
                table: "CreatorPromotionContentSubmissions",
                column: "ReviewMediaAssetId",
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_CreatorPromotionContentSubmission_Source",
                schema: "v3",
                table: "CreatorPromotionContentSubmissions",
                sql: "(\"ReviewMediaAssetId\" IS NOT NULL AND \"Provider\" IS NULL AND \"ContentReference\" IS NULL) OR (\"ReviewMediaAssetId\" IS NULL AND \"Provider\" IN ('TikTok','YouTube','Instagram') AND length(trim(\"ContentReference\")) > 0 AND length(\"ContentReference\") <= 100)");

            migrationBuilder.CreateIndex(
                name: "IX_CreatorPublicationVerifications_CreatorAllocationId",
                schema: "v3",
                table: "CreatorPublicationVerifications",
                column: "CreatorAllocationId",
                unique: true,
                filter: "\"CreatorAllocationId\" IS NOT NULL AND \"Status\" IN ('VerificationPending','Verified')");

            migrationBuilder.CreateIndex(
                name: "IX_CreatorPublicationVerifications_CreatorSocialProfileId",
                schema: "v3",
                table: "CreatorPublicationVerifications",
                column: "CreatorSocialProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_CreatorPublicationVerifications_PromotionContentSubmissionId",
                schema: "v3",
                table: "CreatorPublicationVerifications",
                column: "PromotionContentSubmissionId");

            migrationBuilder.CreateIndex(
                name: "IX_CreatorPublicationVerifications_Status_RequestedAtUtc",
                schema: "v3",
                table: "CreatorPublicationVerifications",
                columns: new[] { "Status", "RequestedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_CreatorPublicationVerifications_UgcAssignmentId",
                schema: "v3",
                table: "CreatorPublicationVerifications",
                column: "UgcAssignmentId",
                unique: true,
                filter: "\"UgcAssignmentId\" IS NOT NULL AND \"Status\" IN ('VerificationPending','Verified')");

            migrationBuilder.CreateIndex(
                name: "IX_CreatorPublicationVerifications_UgcSubmissionId",
                schema: "v3",
                table: "CreatorPublicationVerifications",
                column: "UgcSubmissionId");

            migrationBuilder.CreateIndex(
                name: "IX_PrivateReviewMediaAssets_CreatorAllocationId_RevisionNumber",
                schema: "v3",
                table: "PrivateReviewMediaAssets",
                columns: new[] { "CreatorAllocationId", "RevisionNumber" },
                unique: true,
                filter: "\"CreatorAllocationId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PrivateReviewMediaAssets_StorageKey",
                schema: "v3",
                table: "PrivateReviewMediaAssets",
                column: "StorageKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PrivateReviewMediaAssets_UgcAssignmentId_RevisionNumber",
                schema: "v3",
                table: "PrivateReviewMediaAssets",
                columns: new[] { "UgcAssignmentId", "RevisionNumber" },
                unique: true,
                filter: "\"UgcAssignmentId\" IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_CreatorPromotionContentSubmissions_PrivateReviewMediaAssets~",
                schema: "v3",
                table: "CreatorPromotionContentSubmissions",
                column: "ReviewMediaAssetId",
                principalSchema: "v3",
                principalTable: "PrivateReviewMediaAssets",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_CreatorPromotionParticipations_CreatorPromotionContentSubmi~",
                schema: "v3",
                table: "CreatorPromotionParticipations",
                column: "ApprovedContentSubmissionId",
                principalSchema: "v3",
                principalTable: "CreatorPromotionContentSubmissions",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_CreatorPromotionParticipations_CreatorSocialProfiles_Creato~",
                schema: "v3",
                table: "CreatorPromotionParticipations",
                column: "CreatorSocialProfileId",
                principalSchema: "v3",
                principalTable: "CreatorSocialProfiles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_UgcSubmissions_PrivateReviewMediaAssets_ReviewMediaAssetId",
                schema: "v3",
                table: "UgcSubmissions",
                column: "ReviewMediaAssetId",
                principalSchema: "v3",
                principalTable: "PrivateReviewMediaAssets",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                  IF EXISTS (SELECT 1 FROM v3."CreatorPublicationVerifications")
                     OR EXISTS (SELECT 1 FROM v3."PrivateReviewMediaAssets") THEN
                    RAISE EXCEPTION 'Creator collaboration data exists; restore from a reviewed backup instead of downgrading';
                  END IF;
                END $$;
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_CreatorPromotionContentSubmissions_PrivateReviewMediaAssets~",
                schema: "v3",
                table: "CreatorPromotionContentSubmissions");

            migrationBuilder.DropForeignKey(
                name: "FK_CreatorPromotionParticipations_CreatorPromotionContentSubmi~",
                schema: "v3",
                table: "CreatorPromotionParticipations");

            migrationBuilder.DropForeignKey(
                name: "FK_CreatorPromotionParticipations_CreatorSocialProfiles_Creato~",
                schema: "v3",
                table: "CreatorPromotionParticipations");

            migrationBuilder.DropForeignKey(
                name: "FK_UgcSubmissions_PrivateReviewMediaAssets_ReviewMediaAssetId",
                schema: "v3",
                table: "UgcSubmissions");

            migrationBuilder.DropTable(
                name: "CreatorPublicationVerifications",
                schema: "v3");

            migrationBuilder.DropTable(
                name: "PrivateReviewMediaAssets",
                schema: "v3");

            migrationBuilder.DropIndex(
                name: "IX_UgcSubmissions_ReviewMediaAssetId",
                schema: "v3",
                table: "UgcSubmissions");

            migrationBuilder.DropIndex(
                name: "IX_UgcSubmissions_UgcAssignmentId_ContentRevisionNumber",
                schema: "v3",
                table: "UgcSubmissions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_UgcSubmission_ContentRevision",
                schema: "v3",
                table: "UgcSubmissions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_UgcSubmission_Source",
                schema: "v3",
                table: "UgcSubmissions");

            migrationBuilder.DropIndex(
                name: "IX_CreatorPromotionParticipations_ApprovedContentSubmissionId",
                schema: "v3",
                table: "CreatorPromotionParticipations");

            migrationBuilder.DropIndex(
                name: "IX_CreatorPromotionParticipations_CreatorSocialProfileId",
                schema: "v3",
                table: "CreatorPromotionParticipations");

            migrationBuilder.DropIndex(
                name: "IX_CreatorPromotionContentSubmissions_ReviewMediaAssetId",
                schema: "v3",
                table: "CreatorPromotionContentSubmissions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CreatorPromotionContentSubmission_Source",
                schema: "v3",
                table: "CreatorPromotionContentSubmissions");

            migrationBuilder.DropColumn(
                name: "ContentRevisionNumber",
                schema: "v3",
                table: "UgcSubmissions");

            migrationBuilder.DropColumn(
                name: "ReviewMediaAssetId",
                schema: "v3",
                table: "UgcSubmissions");

            migrationBuilder.DropColumn(
                name: "ApprovedContentSubmissionId",
                schema: "v3",
                table: "CreatorPromotionParticipations");

            migrationBuilder.DropColumn(
                name: "CreatorSocialProfileId",
                schema: "v3",
                table: "CreatorPromotionParticipations");

            migrationBuilder.DropColumn(
                name: "ReviewMediaAssetId",
                schema: "v3",
                table: "CreatorPromotionContentSubmissions");

            migrationBuilder.AlterColumn<string>(
                name: "SubmissionUrl",
                schema: "v3",
                table: "UgcSubmissions",
                type: "text",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(2048)",
                oldMaxLength: 2048,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Provider",
                schema: "v3",
                table: "CreatorPromotionContentSubmissions",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(64)",
                oldMaxLength: 64,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "ContentReference",
                schema: "v3",
                table: "CreatorPromotionContentSubmissions",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100,
                oldNullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_CreatorPromotionContentSubmission_Provider",
                schema: "v3",
                table: "CreatorPromotionContentSubmissions",
                sql: "\"Provider\" IN ('TikTok','YouTube','Instagram')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CreatorPromotionContentSubmission_Reference",
                schema: "v3",
                table: "CreatorPromotionContentSubmissions",
                sql: "length(trim(\"ContentReference\")) > 0 AND length(\"ContentReference\") <= 100");
        }
    }
}
