using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class AlterTableSalesInvoiceItemsDropColumnDeliveryDate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DeliveryDate",
                table: "SALES_INVOICES_ITEMS");

            migrationBuilder.DropColumn(
                name: "NetQuantity",
                table: "SALES_INVOICES_ITEMS");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "DeliveryDate",
                table: "SALES_INVOICES_ITEMS",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "NetQuantity",
                table: "SALES_INVOICES_ITEMS",
                type: "DECIMAL(18,3)",
                nullable: false,
                defaultValue: 0m);
        }
    }
}
