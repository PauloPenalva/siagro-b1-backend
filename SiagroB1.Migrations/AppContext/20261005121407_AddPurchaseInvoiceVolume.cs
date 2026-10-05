using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class AddPurchaseInvoiceVolume : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ReferencedAccessKey",
                table: "PURCHASE_INVOICES");

            migrationBuilder.AddColumn<string>(
                name: "VolumeBrand",
                table: "PURCHASE_INVOICES",
                type: "VARCHAR(60)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VolumeNumbering",
                table: "PURCHASE_INVOICES",
                type: "VARCHAR(60)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "VolumeQuantity",
                table: "PURCHASE_INVOICES",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "VolumeSpecies",
                table: "PURCHASE_INVOICES",
                type: "VARCHAR(60)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "VolumeBrand",
                table: "PURCHASE_INVOICES");

            migrationBuilder.DropColumn(
                name: "VolumeNumbering",
                table: "PURCHASE_INVOICES");

            migrationBuilder.DropColumn(
                name: "VolumeQuantity",
                table: "PURCHASE_INVOICES");

            migrationBuilder.DropColumn(
                name: "VolumeSpecies",
                table: "PURCHASE_INVOICES");

            migrationBuilder.AddColumn<string>(
                name: "ReferencedAccessKey",
                table: "PURCHASE_INVOICES",
                type: "VARCHAR(44)",
                nullable: true);
        }
    }
}
