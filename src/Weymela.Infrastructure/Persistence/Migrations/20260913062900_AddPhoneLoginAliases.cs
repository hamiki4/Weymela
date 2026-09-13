using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Weymela.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPhoneLoginAliases : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EmailIdentifierHash",
                schema: "v3",
                table: "EmailAuthChallenges",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PhoneIdentifierHash",
                schema: "v3",
                table: "EmailAuthChallenges",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DeliveryAddress",
                schema: "v3",
                table: "AuthIdentifiers",
                type: "character varying(320)",
                maxLength: 320,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EmailIdentifierHash",
                schema: "v3",
                table: "EmailAuthChallenges");

            migrationBuilder.DropColumn(
                name: "PhoneIdentifierHash",
                schema: "v3",
                table: "EmailAuthChallenges");

            migrationBuilder.DropColumn(
                name: "DeliveryAddress",
                schema: "v3",
                table: "AuthIdentifiers");
        }
    }
}
