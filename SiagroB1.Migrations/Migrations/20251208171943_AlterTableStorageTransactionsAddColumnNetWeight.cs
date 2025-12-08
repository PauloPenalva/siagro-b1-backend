using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class AlterTableStorageTransactionsAddColumnNetWeight : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DeliveryLocationCode",
                table: "STORAGE_TRANSACTIONS",
                type: "VARCHAR(10)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "NetWeight",
                table: "STORAGE_TRANSACTIONS",
                type: "decimal(18,3)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "UnitOfMeasureCode",
                table: "STORAGE_TRANSACTIONS",
                type: "VARCHAR(4)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_STORAGE_TRANSACTIONS_DeliveryLocationCode",
                table: "STORAGE_TRANSACTIONS",
                column: "DeliveryLocationCode");

            migrationBuilder.CreateIndex(
                name: "IX_STORAGE_TRANSACTIONS_UnitOfMeasureCode",
                table: "STORAGE_TRANSACTIONS",
                column: "UnitOfMeasureCode");

            migrationBuilder.AddForeignKey(
                name: "FK_STORAGE_TRANSACTIONS_UNITS_OF_MEASURE_UnitOfMeasureCode",
                table: "STORAGE_TRANSACTIONS",
                column: "UnitOfMeasureCode",
                principalTable: "UNITS_OF_MEASURE",
                principalColumn: "Code",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_STORAGE_TRANSACTIONS_WAREHOUSES_DeliveryLocationCode",
                table: "STORAGE_TRANSACTIONS",
                column: "DeliveryLocationCode",
                principalTable: "WAREHOUSES",
                principalColumn: "Code",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_STORAGE_TRANSACTIONS_UNITS_OF_MEASURE_UnitOfMeasureCode",
                table: "STORAGE_TRANSACTIONS");

            migrationBuilder.DropForeignKey(
                name: "FK_STORAGE_TRANSACTIONS_WAREHOUSES_DeliveryLocationCode",
                table: "STORAGE_TRANSACTIONS");

            migrationBuilder.DropIndex(
                name: "IX_STORAGE_TRANSACTIONS_DeliveryLocationCode",
                table: "STORAGE_TRANSACTIONS");

            migrationBuilder.DropIndex(
                name: "IX_STORAGE_TRANSACTIONS_UnitOfMeasureCode",
                table: "STORAGE_TRANSACTIONS");

            migrationBuilder.DropColumn(
                name: "DeliveryLocationCode",
                table: "STORAGE_TRANSACTIONS");

            migrationBuilder.DropColumn(
                name: "NetWeight",
                table: "STORAGE_TRANSACTIONS");

            migrationBuilder.DropColumn(
                name: "UnitOfMeasureCode",
                table: "STORAGE_TRANSACTIONS");
        }
    }
}
