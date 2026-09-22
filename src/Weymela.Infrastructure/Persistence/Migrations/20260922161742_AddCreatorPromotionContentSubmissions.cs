using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Weymela.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCreatorPromotionContentSubmissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CreatorPromotionContentSubmissions",
                schema: "v3",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatorAllocationId = table.Column<Guid>(type: "uuid", nullable: false),
                    RevisionNumber = table.Column<int>(type: "integer", nullable: false),
                    Provider = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ContentReference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    SubmittedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ReviewStatus = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Feedback = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ReviewedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReviewedByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CreatorPromotionContentSubmissions", x => x.Id);
                    table.CheckConstraint("CK_CreatorPromotionContentSubmission_ChangesFeedback", "\"ReviewStatus\" <> 'ChangesRequested' OR length(trim(coalesce(\"Feedback\",''))) > 0");
                    table.CheckConstraint("CK_CreatorPromotionContentSubmission_Feedback", "\"Feedback\" IS NULL OR length(\"Feedback\") <= 2000");
                    table.CheckConstraint("CK_CreatorPromotionContentSubmission_Provider", "\"Provider\" IN ('TikTok','YouTube','Instagram')");
                    table.CheckConstraint("CK_CreatorPromotionContentSubmission_Reference", "length(trim(\"ContentReference\")) > 0 AND length(\"ContentReference\") <= 100");
                    table.CheckConstraint("CK_CreatorPromotionContentSubmission_Review", "(\"ReviewStatus\" = 'UnderReview' AND \"ReviewedAtUtc\" IS NULL AND \"ReviewedByUserId\" IS NULL) OR (\"ReviewStatus\" IN ('ChangesRequested','Approved','Rejected') AND \"ReviewedAtUtc\" IS NOT NULL AND \"ReviewedByUserId\" IS NOT NULL)");
                    table.CheckConstraint("CK_CreatorPromotionContentSubmission_Revision", "\"RevisionNumber\" > 0");
                    table.CheckConstraint("CK_CreatorPromotionContentSubmission_Status", "\"ReviewStatus\" IN ('UnderReview','ChangesRequested','Approved','Rejected')");
                    table.ForeignKey(
                        name: "FK_CreatorPromotionContentSubmissions_CreatorAllocations_Creat~",
                        column: x => x.CreatorAllocationId,
                        principalSchema: "v3",
                        principalTable: "CreatorAllocations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CreatorPromotionContentSubmissions_CreatorAllocationId_Revi~",
                schema: "v3",
                table: "CreatorPromotionContentSubmissions",
                columns: new[] { "CreatorAllocationId", "RevisionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CreatorPromotionContentSubmissions_CreatorAllocationId_Subm~",
                schema: "v3",
                table: "CreatorPromotionContentSubmissions",
                columns: new[] { "CreatorAllocationId", "SubmittedAtUtc" });

            migrationBuilder.Sql("""
                CREATE FUNCTION v3.guard_creator_promotion_content_revision() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  IF TG_OP = 'INSERT' THEN
                    IF NEW."ReviewStatus" <> 'UnderReview' OR NEW."ReviewedAtUtc" IS NOT NULL OR NEW."ReviewedByUserId" IS NOT NULL THEN
                      RAISE EXCEPTION 'Promotion content revisions must begin under review' USING ERRCODE='23514';
                    END IF;
                    RETURN NEW;
                  END IF;
                  IF TG_OP = 'DELETE' THEN
                    RAISE EXCEPTION 'Submitted Promotion content revisions are immutable' USING ERRCODE='23514';
                  END IF;
                  IF NEW."Id" <> OLD."Id" OR NEW."CreatorAllocationId" <> OLD."CreatorAllocationId" OR
                     NEW."RevisionNumber" <> OLD."RevisionNumber" OR NEW."Provider" <> OLD."Provider" OR
                     NEW."ContentReference" <> OLD."ContentReference" OR NEW."SubmittedAtUtc" <> OLD."SubmittedAtUtc" THEN
                    RAISE EXCEPTION 'Submitted Promotion content is immutable' USING ERRCODE='23514';
                  END IF;
                  IF OLD."ReviewStatus" <> 'UnderReview' OR NEW."ReviewStatus" NOT IN ('ChangesRequested','Approved','Rejected') THEN
                    RAISE EXCEPTION 'Promotion content review decision is one-time' USING ERRCODE='23514';
                  END IF;
                  RETURN NEW;
                END $$;
                CREATE TRIGGER creator_promotion_content_revision
                  BEFORE INSERT OR UPDATE OR DELETE ON v3."CreatorPromotionContentSubmissions"
                  FOR EACH ROW EXECUTE FUNCTION v3.guard_creator_promotion_content_revision();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS creator_promotion_content_revision ON v3."CreatorPromotionContentSubmissions";
                DROP FUNCTION IF EXISTS v3.guard_creator_promotion_content_revision();
                """);
            migrationBuilder.DropTable(
                name: "CreatorPromotionContentSubmissions",
                schema: "v3");
        }
    }
}
