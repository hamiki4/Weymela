using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Weymela.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProductHandoffTransactions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProductHandoffTransactions",
                schema: "v3",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CodeHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    IdentityBindingId = table.Column<Guid>(type: "uuid", nullable: false),
                    IdentityBindingVersion = table.Column<long>(type: "bigint", nullable: false),
                    Role = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ProfileSubjectId = table.Column<Guid>(type: "uuid", nullable: true),
                    BusinessId = table.Column<Guid>(type: "uuid", nullable: true),
                    Purpose = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Audience = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Environment = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    CallbackId = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    AuthenticatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IssuedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ConsumedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CorrelationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductHandoffTransactions", x => x.Id);
                    table.CheckConstraint("CK_ProductHandoff_Lifetime", "\"ExpiresAtUtc\" > \"IssuedAtUtc\" AND \"ExpiresAtUtc\" <= \"IssuedAtUtc\" + INTERVAL '60 seconds'");
                    table.CheckConstraint("CK_ProductHandoff_Purpose", "\"Purpose\" IN ('PROFILE_ONBOARDING','EXISTING_WORKSPACE')");
                    table.ForeignKey(
                        name: "FK_ProductHandoffTransactions_IdentityBindings_IdentityBinding~",
                        column: x => x.IdentityBindingId,
                        principalSchema: "v3",
                        principalTable: "IdentityBindings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProductHandoffTransactions_CodeHash",
                schema: "v3",
                table: "ProductHandoffTransactions",
                column: "CodeHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductHandoffTransactions_ExpiresAtUtc_ConsumedAtUtc",
                schema: "v3",
                table: "ProductHandoffTransactions",
                columns: new[] { "ExpiresAtUtc", "ConsumedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ProductHandoffTransactions_IdentityBindingId",
                schema: "v3",
                table: "ProductHandoffTransactions",
                column: "IdentityBindingId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProductHandoffTransactions",
                schema: "v3");
        }
    }
}
