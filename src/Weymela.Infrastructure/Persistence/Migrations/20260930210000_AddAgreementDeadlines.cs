using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Weymela.Infrastructure.Persistence;

#nullable disable

namespace Weymela.Infrastructure.Persistence.Migrations;

[DbContext(typeof(WeymelaDbContext))]
[Migration("20260930210000_AddAgreementDeadlines")]
public partial class AddAgreementDeadlines : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTime>(name: "ApplicationClosesAtUtc", schema: "v3", table: "Promotions", type: "timestamp with time zone", nullable: true);
        migrationBuilder.AddColumn<DateTime>(name: "ContentDueAtUtc", schema: "v3", table: "Promotions", type: "timestamp with time zone", nullable: true);
        migrationBuilder.AddColumn<DateTime>(name: "ApplicationClosesAtUtc", schema: "v3", table: "UgcOpportunities", type: "timestamp with time zone", nullable: true);
        migrationBuilder.AddColumn<DateTime>(name: "ContentDueAtUtc", schema: "v3", table: "CreatorAllocations", type: "timestamp with time zone", nullable: true);
        migrationBuilder.AddColumn<DateTime>(name: "ContentDueAtUtc", schema: "v3", table: "UgcAssignments", type: "timestamp with time zone", nullable: true);

        migrationBuilder.AddCheckConstraint(
            name: "CK_Promotion_Application_Content_Deadlines",
            schema: "v3",
            table: "Promotions",
            sql: "(\"ApplicationClosesAtUtc\" IS NULL AND \"ContentDueAtUtc\" IS NULL) OR (\"ApplicationClosesAtUtc\" IS NOT NULL AND \"ContentDueAtUtc\" IS NOT NULL AND \"ApplicationClosesAtUtc\" < \"ContentDueAtUtc\")");
        migrationBuilder.AddCheckConstraint(
            name: "CK_UgcOpportunity_Application_Content_Deadlines",
            schema: "v3",
            table: "UgcOpportunities",
            sql: "\"ApplicationClosesAtUtc\" IS NULL OR \"ApplicationClosesAtUtc\" < \"DueDateUtc\"");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("DO $$ BEGIN IF EXISTS (SELECT 1 FROM v3.\"Promotions\" WHERE \"ApplicationClosesAtUtc\" IS NOT NULL OR \"ContentDueAtUtc\" IS NOT NULL) OR EXISTS (SELECT 1 FROM v3.\"UgcOpportunities\" WHERE \"ApplicationClosesAtUtc\" IS NOT NULL) OR EXISTS (SELECT 1 FROM v3.\"CreatorAllocations\" WHERE \"ContentDueAtUtc\" IS NOT NULL) OR EXISTS (SELECT 1 FROM v3.\"UgcAssignments\" WHERE \"ContentDueAtUtc\" IS NOT NULL) THEN RAISE EXCEPTION 'Restore from backup before dropping agreement deadline snapshots'; END IF; END $$;");
        migrationBuilder.DropCheckConstraint(
            name: "CK_Promotion_Application_Content_Deadlines", schema: "v3", table: "Promotions");
        migrationBuilder.DropCheckConstraint(
            name: "CK_UgcOpportunity_Application_Content_Deadlines", schema: "v3", table: "UgcOpportunities");
        migrationBuilder.DropColumn(name: "ApplicationClosesAtUtc", schema: "v3", table: "Promotions");
        migrationBuilder.DropColumn(name: "ContentDueAtUtc", schema: "v3", table: "Promotions");
        migrationBuilder.DropColumn(name: "ApplicationClosesAtUtc", schema: "v3", table: "UgcOpportunities");
        migrationBuilder.DropColumn(name: "ContentDueAtUtc", schema: "v3", table: "CreatorAllocations");
        migrationBuilder.DropColumn(name: "ContentDueAtUtc", schema: "v3", table: "UgcAssignments");
    }
}
