using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Weymela.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddViewAsSupportSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SupportSessions",
                schema: "v3",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RealActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ViewedUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ViewedRole = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ViewedBusinessId = table.Column<Guid>(type: "uuid", nullable: true),
                    ViewedCreatorId = table.Column<Guid>(type: "uuid", nullable: true),
                    ViewedCustomerId = table.Column<Guid>(type: "uuid", nullable: true),
                    SessionIdentifierHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EndedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupportSessions", x => x.Id);
                    table.CheckConstraint("CK_SupportSession_EndedAt", "\"EndedAtUtc\" IS NULL OR \"EndedAtUtc\" >= \"CreatedAtUtc\"");
                    table.CheckConstraint("CK_SupportSession_Lifetime", "\"ExpiresAtUtc\" > \"CreatedAtUtc\" AND \"ExpiresAtUtc\" <= \"CreatedAtUtc\" + INTERVAL '15 minutes'");
                    table.CheckConstraint("CK_SupportSession_Scope", "(\"ViewedRole\" = 'Business' AND \"ViewedBusinessId\" IS NOT NULL AND \"ViewedCreatorId\" IS NULL AND \"ViewedCustomerId\" IS NULL) OR (\"ViewedRole\" = 'Creator' AND \"ViewedBusinessId\" IS NULL AND \"ViewedCreatorId\" IS NOT NULL AND \"ViewedCustomerId\" IS NULL) OR (\"ViewedRole\" = 'Customer' AND \"ViewedBusinessId\" IS NULL AND \"ViewedCreatorId\" IS NULL AND \"ViewedCustomerId\" IS NOT NULL) OR (\"ViewedRole\" = 'OperationsAdmin' AND \"ViewedBusinessId\" IS NULL AND \"ViewedCreatorId\" IS NULL AND \"ViewedCustomerId\" IS NULL)");
                    table.CheckConstraint("CK_SupportSession_ViewedRole", "\"ViewedRole\" IN ('Customer','Creator','Business','OperationsAdmin')");
                });

            migrationBuilder.CreateIndex(
                name: "IX_SupportSessions_RealActorUserId_CreatedAtUtc",
                schema: "v3",
                table: "SupportSessions",
                columns: new[] { "RealActorUserId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_SupportSessions_RealActorUserId_EndedAtUtc",
                schema: "v3",
                table: "SupportSessions",
                columns: new[] { "RealActorUserId", "EndedAtUtc" },
                unique: true,
                filter: "\"EndedAtUtc\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_SupportSessions_SessionIdentifierHash",
                schema: "v3",
                table: "SupportSessions",
                column: "SessionIdentifierHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SupportSessions_ViewedUserId_CreatedAtUtc",
                schema: "v3",
                table: "SupportSessions",
                columns: new[] { "ViewedUserId", "CreatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SupportSessions",
                schema: "v3");
        }
    }
}
