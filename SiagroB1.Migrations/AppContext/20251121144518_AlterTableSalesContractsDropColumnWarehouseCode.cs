#nullable disable

using Microsoft.EntityFrameworkCore.Migrations;

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class AlterTableSalesContractsDropColumnWarehouseCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SALES_CONTRACTS_WAREHOUSES_DeliveryLocationCode",
                table: "SALES_CONTRACTS");

            migrationBuilder.DropIndex(
                name: "IX_SALES_CONTRACTS_DeliveryLocationCode",
                table: "SALES_CONTRACTS");

            migrationBuilder.DropColumn(
                name: "DeliveryLocationCode",
                table: "SALES_CONTRACTS");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DeliveryLocationCode",
                table: "SALES_CONTRACTS",
                type: "VARCHAR(10)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_SALES_CONTRACTS_DeliveryLocationCode",
                table: "SALES_CONTRACTS",
                column: "DeliveryLocationCode");

            migrationBuilder.AddForeignKey(
                name: "FK_SALES_CONTRACTS_WAREHOUSES_DeliveryLocationCode",
                table: "SALES_CONTRACTS",
                column: "DeliveryLocationCode",
                principalTable: "WAREHOUSES",
                principalColumn: "Code",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
