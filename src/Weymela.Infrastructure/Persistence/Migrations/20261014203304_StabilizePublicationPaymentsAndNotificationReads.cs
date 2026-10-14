using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Weymela.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class StabilizePublicationPaymentsAndNotificationReads : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid[]>(
                name: "RelatedResourceIds",
                schema: "v3",
                table: "InAppNotifications",
                type: "uuid[]",
                nullable: false,
                defaultValue: new Guid[0]);

            migrationBuilder.AddColumn<string>(
                name: "CanonicalUrl",
                schema: "v3",
                table: "CreatorPublicationVerifications",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VerificationCode",
                schema: "v3",
                table: "CreatorPublicationVerifications",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "UgcPaymentCorrections",
                schema: "v3",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UgcAssignmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatorId = table.Column<Guid>(type: "uuid", nullable: false),
                    OriginalEarningEntryId = table.Column<Guid>(type: "uuid", nullable: false),
                    OriginalJournalId = table.Column<Guid>(type: "uuid", nullable: false),
                    CorrectionJournalId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatorAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    PlatformFee = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    AuthorizedBy = table.Column<Guid>(type: "uuid", nullable: false),
                    AuthorizationReference = table.Column<string>(type: "text", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UgcPaymentCorrections", x => x.Id);
                    table.CheckConstraint("CK_UgcCorrection_Amounts", "\"CreatorAmount\" > 0 AND \"PlatformFee\" >= 0");
                    table.ForeignKey(
                        name: "FK_UgcPaymentCorrections_CreatorEarningEntries_OriginalEarning~",
                        column: x => x.OriginalEarningEntryId,
                        principalSchema: "v3",
                        principalTable: "CreatorEarningEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UgcPaymentCorrections_FinancialJournals_CorrectionJournalId",
                        column: x => x.CorrectionJournalId,
                        principalSchema: "v3",
                        principalTable: "FinancialJournals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UgcPaymentCorrections_FinancialJournals_OriginalJournalId",
                        column: x => x.OriginalJournalId,
                        principalSchema: "v3",
                        principalTable: "FinancialJournals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_UgcPaymentCorrections_UgcAssignments_UgcAssignmentId",
                        column: x => x.UgcAssignmentId,
                        principalSchema: "v3",
                        principalTable: "UgcAssignments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UgcPaymentCorrections_CorrectionJournalId",
                schema: "v3",
                table: "UgcPaymentCorrections",
                column: "CorrectionJournalId");

            migrationBuilder.CreateIndex(
                name: "IX_UgcPaymentCorrections_OriginalEarningEntryId",
                schema: "v3",
                table: "UgcPaymentCorrections",
                column: "OriginalEarningEntryId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UgcPaymentCorrections_OriginalJournalId",
                schema: "v3",
                table: "UgcPaymentCorrections",
                column: "OriginalJournalId");

            migrationBuilder.CreateIndex(
                name: "IX_UgcPaymentCorrections_UgcAssignmentId",
                schema: "v3",
                table: "UgcPaymentCorrections",
                column: "UgcAssignmentId",
                unique: true);
            migrationBuilder.Sql(StabilizationIntegritySql.Up);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(StabilizationIntegritySql.Down);

            migrationBuilder.DropTable(
                name: "UgcPaymentCorrections",
                schema: "v3");

            migrationBuilder.DropColumn(
                name: "RelatedResourceIds",
                schema: "v3",
                table: "InAppNotifications");

            migrationBuilder.DropColumn(
                name: "CanonicalUrl",
                schema: "v3",
                table: "CreatorPublicationVerifications");

            migrationBuilder.DropColumn(
                name: "VerificationCode",
                schema: "v3",
                table: "CreatorPublicationVerifications");
        }
    }
}
