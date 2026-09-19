using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Weymela.Infrastructure.Persistence.Migrations;

public partial class AddUgcCustomerDiscountLimit : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<decimal>(
            name: "Ugc_MaximumCustomerDiscountPercent",
            schema: "v3",
            table: "FinancialConfigurationVersions",
            type: "numeric(9,4)",
            precision: 9,
            scale: 4,
            nullable: true);

        migrationBuilder.AddColumn<decimal>(
            name: "PricingSnapshot_MaximumCustomerDiscountPercent",
            schema: "v3",
            table: "UgcOpportunities",
            type: "numeric(9,4)",
            precision: 9,
            scale: 4,
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "Ugc_MaximumCustomerDiscountPercent",
            schema: "v3",
            table: "FinancialConfigurationVersions");

        migrationBuilder.DropColumn(
            name: "PricingSnapshot_MaximumCustomerDiscountPercent",
            schema: "v3",
            table: "UgcOpportunities");
    }
}
