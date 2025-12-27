#nullable disable

using Microsoft.EntityFrameworkCore.Migrations;

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class AlterTablePurchaseContractAddColumnFreightCostStandard : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "FreightCostStandard",
                table: "PURCHASE_CONTRACTS",
                type: "DECIMAL(18,8)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "FreightUmCode",
                table: "PURCHASE_CONTRACTS",
                type: "VARCHAR(4)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PURCHASE_CONTRACTS_FreightUmCode",
                table: "PURCHASE_CONTRACTS",
                column: "FreightUmCode");

            migrationBuilder.AddForeignKey(
                name: "FK_PURCHASE_CONTRACTS_UNITS_OF_MEASURE_FreightUmCode",
                table: "PURCHASE_CONTRACTS",
                column: "FreightUmCode",
                principalTable: "UNITS_OF_MEASURE",
                principalColumn: "Code");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PURCHASE_CONTRACTS_UNITS_OF_MEASURE_FreightUmCode",
                table: "PURCHASE_CONTRACTS");

            migrationBuilder.DropIndex(
                name: "IX_PURCHASE_CONTRACTS_FreightUmCode",
                table: "PURCHASE_CONTRACTS");

            migrationBuilder.DropColumn(
                name: "FreightCostStandard",
                table: "PURCHASE_CONTRACTS");

            migrationBuilder.DropColumn(
                name: "FreightUmCode",
                table: "PURCHASE_CONTRACTS");
        }
    }
}
