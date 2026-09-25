using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Weymela.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPlatformPromotionalFunding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PlatformPromotionalFundings",
                schema: "v3",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    PlatformAdminUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlatformAdminDisplayNameSnapshot = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CorrelationId = table.Column<Guid>(type: "uuid", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    RequestFingerprint = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    JournalId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlatformPromotionalFundings", x => x.Id);
                    table.CheckConstraint("CK_PlatformPromotionalFunding_Valid", "\"Amount\" > 0 AND \"Reason\" <> '' AND \"PlatformAdminDisplayNameSnapshot\" <> ''");
                    table.ForeignKey(
                        name: "FK_PlatformPromotionalFundings_BusinessWallets_BusinessId",
                        column: x => x.BusinessId,
                        principalSchema: "v3",
                        principalTable: "BusinessWallets",
                        principalColumn: "BusinessId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PlatformPromotionalFundings_FinancialJournals_JournalId",
                        column: x => x.JournalId,
                        principalSchema: "v3",
                        principalTable: "FinancialJournals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PlatformPromotionalFundings_BusinessId_CreatedAtUtc",
                schema: "v3",
                table: "PlatformPromotionalFundings",
                columns: new[] { "BusinessId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_PlatformPromotionalFundings_JournalId",
                schema: "v3",
                table: "PlatformPromotionalFundings",
                column: "JournalId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PlatformPromotionalFundings_PlatformAdminUserId_Idempotency~",
                schema: "v3",
                table: "PlatformPromotionalFundings",
                columns: new[] { "PlatformAdminUserId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.Sql("""
                CREATE TRIGGER immutable_history BEFORE UPDATE OR DELETE ON v3."PlatformPromotionalFundings"
                  FOR EACH ROW EXECUTE FUNCTION v3.reject_history_change();

                CREATE FUNCTION v3.guard_platform_promotional_funding_account() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                  IF NEW."Account"='PlatformPromotionalFunding' AND
                    (NEW."Type" <> 'Debit' OR NOT EXISTS (
                      SELECT 1 FROM v3."FinancialJournals" WHERE "Id"=NEW."JournalId" AND "SourceType"='AdminPromotionalFunding')) THEN
                    RAISE EXCEPTION 'Platform promotional funding account is reserved for its dedicated journal source' USING ERRCODE='23514';
                  END IF;
                  RETURN NEW;
                END $$;
                CREATE TRIGGER promotional_funding_account BEFORE INSERT ON v3."FinancialJournalLines"
                  FOR EACH ROW EXECUTE FUNCTION v3.guard_platform_promotional_funding_account();

                CREATE FUNCTION v3.check_admin_promotional_funding() RETURNS trigger LANGUAGE plpgsql AS $$
                DECLARE funding v3."PlatformPromotionalFundings"%ROWTYPE; journal v3."FinancialJournals"%ROWTYPE;
                  line_count integer; debit_amount numeric; credit_amount numeric; movement_count integer;
                BEGIN
                  IF TG_TABLE_NAME = 'FinancialJournals' THEN
                    SELECT * INTO journal FROM v3."FinancialJournals" WHERE "Id"=NEW."Id";
                    SELECT * INTO funding FROM v3."PlatformPromotionalFundings" WHERE "JournalId"=NEW."Id";
                  ELSE
                    funding := NEW;
                    SELECT * INTO journal FROM v3."FinancialJournals" WHERE "Id"=NEW."JournalId";
                  END IF;
                  IF funding."Id" IS NULL OR journal."Id" IS NULL OR journal."SourceType" <> 'AdminPromotionalFunding'
                    OR journal."BusinessId" <> funding."BusinessId" OR journal."ActorId" <> funding."PlatformAdminUserId"
                    OR journal."CorrelationId" <> funding."CorrelationId" OR journal."IdempotencyReference" <> funding."IdempotencyKey" THEN
                    RAISE EXCEPTION 'Promotional funding must reference its matching authoritative journal' USING ERRCODE='23514';
                  END IF;
                  SELECT count(*),
                    coalesce(sum("Amount") FILTER (WHERE "Type"='Debit' AND "Account"='PlatformPromotionalFunding'),0),
                    coalesce(sum("Amount") FILTER (WHERE "Type"='Credit' AND "Account"='BusinessAvailable'),0)
                    INTO line_count, debit_amount, credit_amount
                    FROM v3."FinancialJournalLines" WHERE "JournalId"=journal."Id";
                  SELECT count(*) INTO movement_count FROM v3."WalletEntries"
                    WHERE "JournalId"=journal."Id" AND "BusinessId"=funding."BusinessId"
                      AND "Movement"='AdminPromotionalFunding' AND "Amount"=funding."Amount";
                  IF line_count <> 2 OR debit_amount <> funding."Amount" OR credit_amount <> funding."Amount"
                    OR movement_count <> 1 THEN
                    RAISE EXCEPTION 'Promotional funding journal and wallet movement must match the funding record' USING ERRCODE='23514';
                  END IF;
                  RETURN NULL;
                END $$;
                CREATE CONSTRAINT TRIGGER admin_promotional_funding_record AFTER INSERT ON v3."PlatformPromotionalFundings"
                  DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION v3.check_admin_promotional_funding();
                CREATE CONSTRAINT TRIGGER admin_promotional_funding_journal AFTER INSERT ON v3."FinancialJournals"
                  DEFERRABLE INITIALLY DEFERRED FOR EACH ROW WHEN (NEW."SourceType"='AdminPromotionalFunding')
                  EXECUTE FUNCTION v3.check_admin_promotional_funding();
                REVOKE EXECUTE ON FUNCTION v3.guard_platform_promotional_funding_account() FROM PUBLIC;
                REVOKE EXECUTE ON FUNCTION v3.check_admin_promotional_funding() FROM PUBLIC;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER admin_promotional_funding_journal ON v3."FinancialJournals";
                DROP TRIGGER promotional_funding_account ON v3."FinancialJournalLines";
                DROP FUNCTION v3.guard_platform_promotional_funding_account();
                """);
            migrationBuilder.DropTable(
                name: "PlatformPromotionalFundings",
                schema: "v3");
            migrationBuilder.Sql("DROP FUNCTION v3.check_admin_promotional_funding();");
        }
    }
}
