using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class AddShipmentLoadRefusals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ShipmentLoadRefusalKey",
                table: "SALES_INVOICES",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SHIPMENT_LOAD_REFUSALS",
                columns: table => new
                {
                    Key = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ShipmentLoadKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Destination = table.Column<int>(type: "int", nullable: false),
                    DestinationWarehouseCode = table.Column<string>(type: "VARCHAR(10)", nullable: true),
                    DestinationWarehouseName = table.Column<string>(type: "VARCHAR(200)", nullable: true),
                    Reason = table.Column<string>(type: "VARCHAR(500)", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "VARCHAR(100)", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CompletedBy = table.Column<string>(type: "VARCHAR(100)", nullable: true),
                    CancelledAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CancelledBy = table.Column<string>(type: "VARCHAR(100)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SHIPMENT_LOAD_REFUSALS", x => x.Key);
                    table.ForeignKey(
                        name: "FK_SHIPMENT_LOAD_REFUSALS_SHIPMENT_LOADS_ShipmentLoadKey",
                        column: x => x.ShipmentLoadKey,
                        principalTable: "SHIPMENT_LOADS",
                        principalColumn: "Key");
                });

            migrationBuilder.CreateIndex(
                name: "IX_SALES_INVOICES_ShipmentLoadRefusalKey",
                table: "SALES_INVOICES",
                column: "ShipmentLoadRefusalKey");

            migrationBuilder.CreateIndex(
                name: "IX_SHIPMENT_LOAD_REFUSALS_ShipmentLoadKey",
                table: "SHIPMENT_LOAD_REFUSALS",
                column: "ShipmentLoadKey",
                unique: true,
                filter: "[Status] = 0");

            migrationBuilder.AddForeignKey(
                name: "FK_SALES_INVOICES_SHIPMENT_LOAD_REFUSALS_ShipmentLoadRefusalKey",
                table: "SALES_INVOICES",
                column: "ShipmentLoadRefusalKey",
                principalTable: "SHIPMENT_LOAD_REFUSALS",
                principalColumn: "Key");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SALES_INVOICES_SHIPMENT_LOAD_REFUSALS_ShipmentLoadRefusalKey",
                table: "SALES_INVOICES");

            migrationBuilder.DropTable(
                name: "SHIPMENT_LOAD_REFUSALS");

            migrationBuilder.DropIndex(
                name: "IX_SALES_INVOICES_ShipmentLoadRefusalKey",
                table: "SALES_INVOICES");

            migrationBuilder.DropColumn(
                name: "ShipmentLoadRefusalKey",
                table: "SALES_INVOICES");
        }
    }
}
