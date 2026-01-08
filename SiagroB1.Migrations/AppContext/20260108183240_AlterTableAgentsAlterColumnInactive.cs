using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class AlterTableAgentsAlterColumnInactive : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PURCHASE_CONTRACTS_UNITS_OF_MEASURE_FreightUmCode",
                table: "PURCHASE_CONTRACTS");

            migrationBuilder.DropForeignKey(
                name: "FK_PURCHASE_CONTRACTS_UNITS_OF_MEASURE_UnitOfMeasureCode",
                table: "PURCHASE_CONTRACTS");

            migrationBuilder.DropForeignKey(
                name: "FK_PURCHASE_CONTRACTS_WAREHOUSES_DeliveryLocationCode",
                table: "PURCHASE_CONTRACTS");

            migrationBuilder.DropIndex(
                name: "IX_PURCHASE_CONTRACTS_DeliveryLocationCode",
                table: "PURCHASE_CONTRACTS");

            migrationBuilder.DropIndex(
                name: "IX_PURCHASE_CONTRACTS_FreightUmCode",
                table: "PURCHASE_CONTRACTS");

            migrationBuilder.DropIndex(
                name: "IX_PURCHASE_CONTRACTS_UnitOfMeasureCode",
                table: "PURCHASE_CONTRACTS");

            migrationBuilder.RenameColumn(
                name: "Inactive",
                table: "AGENTS",
                newName: "Locked");

            migrationBuilder.AlterColumn<string>(
                name: "Locked",
                table: "AGENTS",
                type: "VARCHAR(1)",
                nullable: true,
                oldClrType: typeof(bool),
                oldType: "bit");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Locked",
                table: "AGENTS",
                newName: "Inactive");

            migrationBuilder.AlterColumn<bool>(
                name: "Inactive",
                table: "AGENTS",
                type: "bit",
                nullable: false,
                defaultValue: false,
                oldClrType: typeof(string),
                oldType: "VARCHAR(1)",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PURCHASE_CONTRACTS_DeliveryLocationCode",
                table: "PURCHASE_CONTRACTS",
                column: "DeliveryLocationCode");

            migrationBuilder.CreateIndex(
                name: "IX_PURCHASE_CONTRACTS_FreightUmCode",
                table: "PURCHASE_CONTRACTS",
                column: "FreightUmCode");

            migrationBuilder.CreateIndex(
                name: "IX_PURCHASE_CONTRACTS_UnitOfMeasureCode",
                table: "PURCHASE_CONTRACTS",
                column: "UnitOfMeasureCode");

            migrationBuilder.AddForeignKey(
                name: "FK_PURCHASE_CONTRACTS_UNITS_OF_MEASURE_FreightUmCode",
                table: "PURCHASE_CONTRACTS",
                column: "FreightUmCode",
                principalTable: "UNITS_OF_MEASURE",
                principalColumn: "Code");

            migrationBuilder.AddForeignKey(
                name: "FK_PURCHASE_CONTRACTS_UNITS_OF_MEASURE_UnitOfMeasureCode",
                table: "PURCHASE_CONTRACTS",
                column: "UnitOfMeasureCode",
                principalTable: "UNITS_OF_MEASURE",
                principalColumn: "Code");

            migrationBuilder.AddForeignKey(
                name: "FK_PURCHASE_CONTRACTS_WAREHOUSES_DeliveryLocationCode",
                table: "PURCHASE_CONTRACTS",
                column: "DeliveryLocationCode",
                principalTable: "WAREHOUSES",
                principalColumn: "Code");
        }
    }
}
