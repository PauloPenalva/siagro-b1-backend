using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class AddItemCestAndSalesInvoiceVolume : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "VolumeBrand",
                table: "SALES_INVOICES",
                type: "VARCHAR(60)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VolumeNumbering",
                table: "SALES_INVOICES",
                type: "VARCHAR(60)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "VolumeQuantity",
                table: "SALES_INVOICES",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VolumeSpecies",
                table: "SALES_INVOICES",
                type: "VARCHAR(60)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Cest",
                table: "ITEMS",
                type: "VARCHAR(7)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "VolumeBrand",
                table: "SALES_INVOICES");

            migrationBuilder.DropColumn(
                name: "VolumeNumbering",
                table: "SALES_INVOICES");

            migrationBuilder.DropColumn(
                name: "VolumeQuantity",
                table: "SALES_INVOICES");

            migrationBuilder.DropColumn(
                name: "VolumeSpecies",
                table: "SALES_INVOICES");

            migrationBuilder.DropColumn(
                name: "Cest",
                table: "ITEMS");
        }
    }
}
