using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Weymela.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOperationalSecurityAndNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "FailedAtUtc",
                schema: "v3",
                table: "OutboxMessages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "FailureCount",
                schema: "v3",
                table: "OutboxMessages",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "NextAttemptAtUtc",
                schema: "v3",
                table: "OutboxMessages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RecipientCursor",
                schema: "v3",
                table: "OutboxMessages",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DepositRequests",
                schema: "v3",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubmittedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Provider = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    ExternalReference = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    ProofReference = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    Status = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SubmittedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ReviewedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReviewedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    ConfirmationReference = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    JournalId = table.Column<Guid>(type: "uuid", nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DepositRequests", x => x.Id);
                    table.CheckConstraint("CK_DepositRequest_Journal", "(\"Status\"='Approved' AND \"JournalId\" IS NOT NULL) OR (\"Status\" IN ('Pending','Rejected') AND \"JournalId\" IS NULL)");
                    table.CheckConstraint("CK_DepositRequest_Positive", "\"Amount\" > 0");
                    table.CheckConstraint("CK_DepositRequest_Review", "(\"Status\"='Pending' AND \"ReviewedBy\" IS NULL AND \"ReviewedAtUtc\" IS NULL) OR (\"Status\" IN ('Approved','Rejected') AND \"ReviewedBy\" IS NOT NULL AND \"ReviewedAtUtc\" IS NOT NULL AND \"ConfirmationReference\" IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_DepositRequests_BusinessWallets_BusinessId",
                        column: x => x.BusinessId,
                        principalSchema: "v3",
                        principalTable: "BusinessWallets",
                        principalColumn: "BusinessId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DepositRequests_FinancialJournals_JournalId",
                        column: x => x.JournalId,
                        principalSchema: "v3",
                        principalTable: "FinancialJournals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "IdentityBindings",
                schema: "v3",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    ProjectId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    ExternalSubject = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    ValidAfterUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IdentityBindings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "InAppNotifications",
                schema: "v3",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Role = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    SourceKey = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    EventType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Title = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    Message = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Route = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ReadAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PushState = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PushAttempts = table.Column<int>(type: "integer", nullable: false),
                    NextPushAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastPushErrorCode = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InAppNotifications", x => x.Id);
                    table.CheckConstraint("CK_Notification_ReadTime", "\"ReadAtUtc\" IS NULL OR \"ReadAtUtc\" >= \"CreatedAtUtc\"");
                });

            migrationBuilder.CreateTable(
                name: "PublicWorkspaceProfiles",
                schema: "v3",
                columns: table => new
                {
                    SubjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    Role = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    PublicId = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Region = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Category = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    VerifiedFollowers = table.Column<long>(type: "bigint", nullable: false),
                    VerifiedViews = table.Column<long>(type: "bigint", nullable: false),
                    SocialVerified = table.Column<bool>(type: "boolean", nullable: false),
                    PortfolioUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    DirectionsUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PublicWorkspaceProfiles", x => new { x.SubjectId, x.Role });
                    table.CheckConstraint("CK_PublicProfile_Metrics", "\"VerifiedFollowers\" >= 0 AND \"VerifiedViews\" >= 0");
                });

            migrationBuilder.CreateTable(
                name: "WorkerCheckpoints",
                schema: "v3",
                columns: table => new
                {
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    LastEffectiveConfigurationId = table.Column<Guid>(type: "uuid", nullable: true),
                    LastSeenAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastSuccessAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastErrorCode = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: true),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkerCheckpoints", x => x.Name);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_NextAttemptAtUtc",
                schema: "v3",
                table: "OutboxMessages",
                column: "NextAttemptAtUtc",
                filter: "\"ProcessedAtUtc\" IS NULL AND \"FailedAtUtc\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_OfferQrSessions_ExpiresAtUtc",
                schema: "v3",
                table: "OfferQrSessions",
                column: "ExpiresAtUtc",
                filter: "\"Status\"='Issued'");

            migrationBuilder.CreateIndex(
                name: "IX_DepositRequests_BusinessId_Provider_ExternalReference",
                schema: "v3",
                table: "DepositRequests",
                columns: new[] { "BusinessId", "Provider", "ExternalReference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DepositRequests_JournalId",
                schema: "v3",
                table: "DepositRequests",
                column: "JournalId");

            migrationBuilder.CreateIndex(
                name: "IX_DepositRequests_Provider_ConfirmationReference",
                schema: "v3",
                table: "DepositRequests",
                columns: new[] { "Provider", "ConfirmationReference" },
                unique: true,
                filter: "\"Status\"='Approved'");

            migrationBuilder.CreateIndex(
                name: "IX_DepositRequests_Status_SubmittedAtUtc",
                schema: "v3",
                table: "DepositRequests",
                columns: new[] { "Status", "SubmittedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_IdentityBindings_Provider_ProjectId_ExternalSubject",
                schema: "v3",
                table: "IdentityBindings",
                columns: new[] { "Provider", "ProjectId", "ExternalSubject" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_IdentityBindings_UserId",
                schema: "v3",
                table: "IdentityBindings",
                column: "UserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InAppNotifications_NextPushAtUtc",
                schema: "v3",
                table: "InAppNotifications",
                column: "NextPushAtUtc",
                filter: "\"PushState\"='Pending'");

            migrationBuilder.CreateIndex(
                name: "IX_InAppNotifications_UserId_Role_CreatedAtUtc",
                schema: "v3",
                table: "InAppNotifications",
                columns: new[] { "UserId", "Role", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_InAppNotifications_UserId_Role_SourceKey",
                schema: "v3",
                table: "InAppNotifications",
                columns: new[] { "UserId", "Role", "SourceKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PublicWorkspaceProfiles_Role_PublicId",
                schema: "v3",
                table: "PublicWorkspaceProfiles",
                columns: new[] { "Role", "PublicId" },
                unique: true);
            migrationBuilder.Sql(Phase6IntegritySql.Create);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DepositRequests",
                schema: "v3");

            migrationBuilder.DropTable(
                name: "IdentityBindings",
                schema: "v3");

            migrationBuilder.DropTable(
                name: "InAppNotifications",
                schema: "v3");

            migrationBuilder.DropTable(
                name: "PublicWorkspaceProfiles",
                schema: "v3");

            migrationBuilder.DropTable(
                name: "WorkerCheckpoints",
                schema: "v3");

            migrationBuilder.DropIndex(
                name: "IX_OutboxMessages_NextAttemptAtUtc",
                schema: "v3",
                table: "OutboxMessages");

            migrationBuilder.DropIndex(
                name: "IX_OfferQrSessions_ExpiresAtUtc",
                schema: "v3",
                table: "OfferQrSessions");

            migrationBuilder.DropColumn(
                name: "FailedAtUtc",
                schema: "v3",
                table: "OutboxMessages");

            migrationBuilder.DropColumn(
                name: "FailureCount",
                schema: "v3",
                table: "OutboxMessages");

            migrationBuilder.DropColumn(
                name: "NextAttemptAtUtc",
                schema: "v3",
                table: "OutboxMessages");

            migrationBuilder.DropColumn(
                name: "RecipientCursor",
                schema: "v3",
                table: "OutboxMessages");
        }
    }
}
