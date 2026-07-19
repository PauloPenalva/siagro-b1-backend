using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class DropUnusedUnitOfMeasureNames : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "UnitOfMeasureName",
                table: "STORAGE_TRANSACTIONS");

            migrationBuilder.DropColumn(
                name: "UoMName",
                table: "STORAGE_ADDRESSES");

            migrationBuilder.DropColumn(
                name: "UnitOfMeasureName",
                table: "SALES_CONTRACTS");

            migrationBuilder.DropColumn(
                name: "UnitOfMeasureName",
                table: "PURCHASE_CONTRACTS");

            migrationBuilder.DropColumn(
                name: "UomName",
                table: "OWNERSHIP_TRANSFER");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "UnitOfMeasureName",
                table: "STORAGE_TRANSACTIONS",
                type: "VARCHAR(100)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UoMName",
                table: "STORAGE_ADDRESSES",
                type: "VARCHAR(100)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UnitOfMeasureName",
                table: "SALES_CONTRACTS",
                type: "VARCHAR(100)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UnitOfMeasureName",
                table: "PURCHASE_CONTRACTS",
                type: "VARCHAR(100)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "UomName",
                table: "OWNERSHIP_TRANSFER",
                type: "VARCHAR(100)",
                nullable: true);
        }
    }
}
