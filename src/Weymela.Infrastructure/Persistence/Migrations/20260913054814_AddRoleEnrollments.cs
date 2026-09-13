using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Weymela.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRoleEnrollments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RoleEnrollments",
                schema: "v3",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    RequestedRole = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ProposedBusinessId = table.Column<Guid>(type: "uuid", nullable: true),
                    SubmissionJson = table.Column<string>(type: "character varying(6000)", maxLength: 6000, nullable: false),
                    Status = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SubmittedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ReviewedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReviewedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    DecisionReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    IdempotencyKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoleEnrollments", x => x.Id);
                    table.CheckConstraint("CK_RoleEnrollment_Status", "\"Status\" IN ('Pending','Approved','Rejected')");
                });

            migrationBuilder.CreateIndex(
                name: "IX_RoleEnrollments_UserId_IdempotencyKey",
                schema: "v3",
                table: "RoleEnrollments",
                columns: new[] { "UserId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RoleEnrollments_UserId_RequestedRole_Status",
                schema: "v3",
                table: "RoleEnrollments",
                columns: new[] { "UserId", "RequestedRole", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RoleEnrollments",
                schema: "v3");
        }
    }
}
