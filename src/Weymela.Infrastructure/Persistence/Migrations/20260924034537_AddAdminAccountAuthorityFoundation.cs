using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Weymela.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAdminAccountAuthorityFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Operation",
                schema: "v3",
                table: "AuditEvents",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Reason",
                schema: "v3",
                table: "AuditEvents",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SupportSessionId",
                schema: "v3",
                table: "AuditEvents",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TargetRole",
                schema: "v3",
                table: "AuditEvents",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TargetSubjectId",
                schema: "v3",
                table: "AuditEvents",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TargetUserId",
                schema: "v3",
                table: "AuditEvents",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AccountLifecycles",
                schema: "v3",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    ChangedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountLifecycles", x => x.UserId);
                    table.CheckConstraint("CK_AccountLifecycle_Reason", "\"Reason\" IS NULL OR char_length(btrim(\"Reason\")) BETWEEN 1 AND 500");
                });

            migrationBuilder.CreateTable(
                name: "AccountPreauthorizations",
                schema: "v3",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetRole = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    EmailIdentifierHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    PhoneIdentifierHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    DisplayName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    PublicId = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    Region = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    Category = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    SubmissionJson = table.Column<string>(type: "character varying(6000)", maxLength: 6000, nullable: true),
                    Status = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CreatedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ActivationSecretHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    ActivationSecretExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ActivatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CancelledAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DisabledAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountPreauthorizations", x => x.Id);
                    table.CheckConstraint("CK_AccountPreauthorization_Expiry", "\"ExpiresAtUtc\" > \"CreatedAtUtc\" AND \"ActivationSecretExpiresAtUtc\" = \"ExpiresAtUtc\"");
                });

            migrationBuilder.CreateTable(
                name: "AccountRoleHistory",
                schema: "v3",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ActorUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetRole = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TargetSubjectId = table.Column<Guid>(type: "uuid", nullable: true),
                    BusinessId = table.Column<Guid>(type: "uuid", nullable: true),
                    Action = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    Reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    OccurredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CorrelationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReferenceId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AccountRoleHistory", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_SupportSessionId_OccurredAtUtc",
                schema: "v3",
                table: "AuditEvents",
                columns: new[] { "SupportSessionId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_TargetUserId_OccurredAtUtc",
                schema: "v3",
                table: "AuditEvents",
                columns: new[] { "TargetUserId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AccountLifecycles_Status_UpdatedAtUtc",
                schema: "v3",
                table: "AccountLifecycles",
                columns: new[] { "Status", "UpdatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AccountPreauthorizations_EmailIdentifierHash_TargetRole_Sta~",
                schema: "v3",
                table: "AccountPreauthorizations",
                columns: new[] { "EmailIdentifierHash", "TargetRole", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_AccountPreauthorizations_UserId_TargetRole_Status",
                schema: "v3",
                table: "AccountPreauthorizations",
                columns: new[] { "UserId", "TargetRole", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_AccountRoleHistory_ActorUserId_OccurredAtUtc",
                schema: "v3",
                table: "AccountRoleHistory",
                columns: new[] { "ActorUserId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_AccountRoleHistory_TargetUserId_OccurredAtUtc",
                schema: "v3",
                table: "AccountRoleHistory",
                columns: new[] { "TargetUserId", "OccurredAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AccountLifecycles",
                schema: "v3");

            migrationBuilder.DropTable(
                name: "AccountPreauthorizations",
                schema: "v3");

            migrationBuilder.DropTable(
                name: "AccountRoleHistory",
                schema: "v3");

            migrationBuilder.DropIndex(
                name: "IX_AuditEvents_SupportSessionId_OccurredAtUtc",
                schema: "v3",
                table: "AuditEvents");

            migrationBuilder.DropIndex(
                name: "IX_AuditEvents_TargetUserId_OccurredAtUtc",
                schema: "v3",
                table: "AuditEvents");

            migrationBuilder.DropColumn(
                name: "Operation",
                schema: "v3",
                table: "AuditEvents");

            migrationBuilder.DropColumn(
                name: "Reason",
                schema: "v3",
                table: "AuditEvents");

            migrationBuilder.DropColumn(
                name: "SupportSessionId",
                schema: "v3",
                table: "AuditEvents");

            migrationBuilder.DropColumn(
                name: "TargetRole",
                schema: "v3",
                table: "AuditEvents");

            migrationBuilder.DropColumn(
                name: "TargetSubjectId",
                schema: "v3",
                table: "AuditEvents");

            migrationBuilder.DropColumn(
                name: "TargetUserId",
                schema: "v3",
                table: "AuditEvents");
        }
    }
}
