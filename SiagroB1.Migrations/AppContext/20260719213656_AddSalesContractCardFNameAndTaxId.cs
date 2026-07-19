using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class AddSalesContractCardFNameAndTaxId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CardFName",
                table: "SALES_CONTRACTS",
                type: "VARCHAR(200)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CardTaxId",
                table: "SALES_CONTRACTS",
                type: "VARCHAR(20)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CardFName",
                table: "SALES_CONTRACTS");

            migrationBuilder.DropColumn(
                name: "CardTaxId",
                table: "SALES_CONTRACTS");
        }
    }
}
