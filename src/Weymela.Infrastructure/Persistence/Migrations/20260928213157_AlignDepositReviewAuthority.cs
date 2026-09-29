using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Weymela.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AlignDepositReviewAuthority : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Replace only the review guard body. The existing trigger, journal constraint,
            // immutable history, ownership and function privileges remain in place.
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION v3.guard_deposit_review() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  IF TG_OP='INSERT' THEN
                    IF NEW."Status"<>'Pending' THEN RAISE EXCEPTION 'Deposit must begin pending' USING ERRCODE='23514'; END IF;
                    RETURN NEW;
                  END IF;
                  IF OLD."Status"<>'Pending' OR NEW."Status" NOT IN ('Approved','Rejected') OR NEW."Id"<>OLD."Id" OR
                     NEW."BusinessId"<>OLD."BusinessId" OR NEW."SubmittedBy"<>OLD."SubmittedBy" OR NEW."Amount"<>OLD."Amount" OR
                     NEW."Provider"<>OLD."Provider" OR NEW."ExternalReference"<>OLD."ExternalReference" OR
                     NEW."ProofReference" IS DISTINCT FROM OLD."ProofReference" OR NEW."SubmittedAtUtc"<>OLD."SubmittedAtUtc" OR
                     NEW."ReviewedAtUtc"<OLD."SubmittedAtUtc" OR NOT EXISTS
                       (SELECT 1 FROM v3."CommercePermissions" p WHERE p."UserId"=NEW."ReviewedBy"
                        AND p."Role" IN ('PlatformAdmin','OperationsAdmin') AND p."IsActive") THEN
                    RAISE EXCEPTION 'Deposit requires one authorized immutable review' USING ERRCODE='23514';
                  END IF;
                  RETURN NEW;
                END $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Restore the previous guard when an isolated migration test steps back.
            // Production restore/redeployment still follows the reviewed backup runbook.
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION v3.guard_deposit_review() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  IF TG_OP='INSERT' THEN
                    IF NEW."Status"<>'Pending' THEN RAISE EXCEPTION 'Deposit must begin pending' USING ERRCODE='23514'; END IF;
                    RETURN NEW;
                  END IF;
                  IF OLD."Status"<>'Pending' OR NEW."Status" NOT IN ('Approved','Rejected') OR NEW."Id"<>OLD."Id" OR
                     NEW."BusinessId"<>OLD."BusinessId" OR NEW."SubmittedBy"<>OLD."SubmittedBy" OR NEW."Amount"<>OLD."Amount" OR
                     NEW."Provider"<>OLD."Provider" OR NEW."ExternalReference"<>OLD."ExternalReference" OR
                     NEW."ProofReference" IS DISTINCT FROM OLD."ProofReference" OR NEW."SubmittedAtUtc"<>OLD."SubmittedAtUtc" OR
                     NEW."ReviewedAtUtc"<OLD."SubmittedAtUtc" OR NOT EXISTS
                       (SELECT 1 FROM v3."CommercePermissions" p WHERE p."UserId"=NEW."ReviewedBy"
                        AND p."Role"='PlatformAdmin' AND p."IsActive") THEN
                    RAISE EXCEPTION 'Deposit requires one authorized immutable review' USING ERRCODE='23514';
                  END IF;
                  RETURN NEW;
                END $$;
                """);
        }
    }
}
