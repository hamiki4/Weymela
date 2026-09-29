using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Weymela.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCreatorNumbers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "CreatorNumber",
                schema: "v3",
                table: "PublicWorkspaceProfiles",
                type: "bigint",
                nullable: true,
                defaultValueSql: "NULL");

            // PublicId/SubjectId gives an explicit, stable order for the one-time backfill.
            // Allocation after that point is owned by PostgreSQL, including concurrent inserts.
            migrationBuilder.Sql("""
                CREATE SEQUENCE v3.creator_number_seq AS bigint START WITH 1000 MINVALUE 1000 NO CYCLE
                  OWNED BY v3."PublicWorkspaceProfiles"."CreatorNumber";
                WITH ordered AS (
                  SELECT "SubjectId", "Role",
                    999 + row_number() OVER (ORDER BY "PublicId", "SubjectId") AS number
                  FROM v3."PublicWorkspaceProfiles" WHERE "Role"='Creator'
                )
                UPDATE v3."PublicWorkspaceProfiles" p SET "CreatorNumber"=ordered.number
                FROM ordered WHERE p."SubjectId"=ordered."SubjectId" AND p."Role"=ordered."Role";
                DO $creator_seq$ BEGIN
                  PERFORM setval('v3.creator_number_seq',
                    greatest(coalesce((SELECT max("CreatorNumber") FROM v3."PublicWorkspaceProfiles" WHERE "Role"='Creator'),1000),1000),
                    EXISTS (SELECT 1 FROM v3."PublicWorkspaceProfiles" WHERE "Role"='Creator'));
                END $creator_seq$;
                CREATE FUNCTION v3.assign_creator_number() RETURNS trigger LANGUAGE plpgsql
                  SECURITY DEFINER SET search_path = pg_catalog, v3 AS $$
                BEGIN
                  IF TG_OP='INSERT' THEN
                    IF NEW."CreatorNumber" IS NOT NULL THEN
                      RAISE EXCEPTION 'Creator ID is server assigned' USING ERRCODE='23514';
                    END IF;
                    IF NEW."Role"='Creator' THEN
                      NEW."CreatorNumber" := nextval('v3.creator_number_seq'::regclass);
                    END IF;
                  ELSIF NEW."CreatorNumber" IS DISTINCT FROM OLD."CreatorNumber"
                     OR NEW."Role" IS DISTINCT FROM OLD."Role"
                     OR NEW."SubjectId" IS DISTINCT FROM OLD."SubjectId" THEN
                    RAISE EXCEPTION 'Creator ID is immutable' USING ERRCODE='23514';
                  END IF;
                  RETURN NEW;
                END $$;
                REVOKE ALL ON FUNCTION v3.assign_creator_number() FROM PUBLIC;
                CREATE TRIGGER assign_creator_number BEFORE INSERT OR UPDATE ON v3."PublicWorkspaceProfiles"
                  FOR EACH ROW EXECUTE FUNCTION v3.assign_creator_number();
                """);

            migrationBuilder.CreateIndex(
                name: "IX_PublicWorkspaceProfiles_CreatorNumber",
                schema: "v3",
                table: "PublicWorkspaceProfiles",
                column: "CreatorNumber",
                unique: true,
                filter: "\"CreatorNumber\" IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PublicProfile_CreatorNumber",
                schema: "v3",
                table: "PublicWorkspaceProfiles",
                sql: "(\"Role\"='Creator' AND \"CreatorNumber\" IS NOT NULL AND \"CreatorNumber\" >= 1000) OR (\"Role\"<>'Creator' AND \"CreatorNumber\" IS NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER assign_creator_number ON v3."PublicWorkspaceProfiles";
                DROP FUNCTION v3.assign_creator_number();
                """);
            migrationBuilder.DropIndex(
                name: "IX_PublicWorkspaceProfiles_CreatorNumber",
                schema: "v3",
                table: "PublicWorkspaceProfiles");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PublicProfile_CreatorNumber",
                schema: "v3",
                table: "PublicWorkspaceProfiles");

            migrationBuilder.DropColumn(
                name: "CreatorNumber",
                schema: "v3",
                table: "PublicWorkspaceProfiles");
        }
    }
}
