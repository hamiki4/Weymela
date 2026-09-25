using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Weymela.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RetireSupportSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Historical support-session rows are security evidence. Archive them before
            // removing the runtime table; AuditEvents.SupportSessionId remains intact.
            migrationBuilder.Sql("""
                CREATE TABLE v3."RetiredSupportSessions" (LIKE v3."SupportSessions" INCLUDING ALL);
                INSERT INTO v3."RetiredSupportSessions" SELECT * FROM v3."SupportSessions";
                REVOKE ALL ON TABLE v3."RetiredSupportSessions" FROM PUBLIC;
                CREATE TRIGGER immutable_history BEFORE UPDATE OR DELETE ON v3."RetiredSupportSessions"
                  FOR EACH ROW EXECUTE FUNCTION v3.reject_history_change();
                """);
            migrationBuilder.DropTable(
                name: "SupportSessions",
                schema: "v3");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE TABLE v3."SupportSessions" (LIKE v3."RetiredSupportSessions" INCLUDING ALL);
                INSERT INTO v3."SupportSessions" SELECT * FROM v3."RetiredSupportSessions";
                DROP TABLE v3."RetiredSupportSessions";
                """);
        }
    }
}
