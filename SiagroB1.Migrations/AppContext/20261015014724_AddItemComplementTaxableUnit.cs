using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class AddItemComplementTaxableUnit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "TaxableFactor",
                table: "ITEM_COMPLEMENTS",
                type: "DECIMAL(18,6)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TaxableUnitOfMeasureCode",
                table: "ITEM_COMPLEMENTS",
                type: "VARCHAR(6)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TaxableFactor",
                table: "ITEM_COMPLEMENTS");

            migrationBuilder.DropColumn(
                name: "TaxableUnitOfMeasureCode",
                table: "ITEM_COMPLEMENTS");
        }
    }
}
