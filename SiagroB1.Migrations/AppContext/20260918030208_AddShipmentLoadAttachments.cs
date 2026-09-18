using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class AddShipmentLoadAttachments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "DischargedQuantity",
                table: "SHIPMENT_LOADS",
                type: "DECIMAL(18,3)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "TicketDeliveredQuantity",
                table: "SALES_INVOICES_ITEMS",
                type: "DECIMAL(18,3)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateTable(
                name: "SHIPMENT_LOAD_ATTACHMENTS",
                columns: table => new
                {
                    Key = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ShipmentLoadKey = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    AttachmentType = table.Column<int>(type: "int", nullable: false),
                    Description = table.Column<string>(type: "VARCHAR(100)", nullable: false),
                    FileName = table.Column<string>(type: "VARCHAR(100)", nullable: false),
                    FileData = table.Column<byte[]>(type: "VARBINARY(MAX)", nullable: false),
                    ContentType = table.Column<string>(type: "VARCHAR(100)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<string>(type: "VARCHAR(100)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SHIPMENT_LOAD_ATTACHMENTS", x => x.Key);
                    table.ForeignKey(
                        name: "FK_SHIPMENT_LOAD_ATTACHMENTS_SHIPMENT_LOADS_ShipmentLoadKey",
                        column: x => x.ShipmentLoadKey,
                        principalTable: "SHIPMENT_LOADS",
                        principalColumn: "Key");
                });

            migrationBuilder.CreateTable(
                name: "SHIPMENT_LOAD_DISCHARGES",
                columns: table => new
                {
                    Key = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ShipmentLoadKey = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SalesInvoiceKey = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    SalesInvoiceItemKey = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    TicketNumber = table.Column<string>(type: "VARCHAR(50)", nullable: false),
                    DischargeDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DischargedQuantity = table.Column<decimal>(type: "DECIMAL(18,3)", nullable: false),
                    Comments = table.Column<string>(type: "VARCHAR(500)", nullable: true),
                    AttachmentKey = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<string>(type: "VARCHAR(100)", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<string>(type: "VARCHAR(100)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SHIPMENT_LOAD_DISCHARGES", x => x.Key);
                    table.ForeignKey(
                        name: "FK_SHIPMENT_LOAD_DISCHARGES_SALES_INVOICES_ITEMS_SalesInvoiceItemKey",
                        column: x => x.SalesInvoiceItemKey,
                        principalTable: "SALES_INVOICES_ITEMS",
                        principalColumn: "Key");
                    table.ForeignKey(
                        name: "FK_SHIPMENT_LOAD_DISCHARGES_SALES_INVOICES_SalesInvoiceKey",
                        column: x => x.SalesInvoiceKey,
                        principalTable: "SALES_INVOICES",
                        principalColumn: "Key");
                    table.ForeignKey(
                        name: "FK_SHIPMENT_LOAD_DISCHARGES_SHIPMENT_LOADS_ShipmentLoadKey",
                        column: x => x.ShipmentLoadKey,
                        principalTable: "SHIPMENT_LOADS",
                        principalColumn: "Key");
                    table.ForeignKey(
                        name: "FK_SHIPMENT_LOAD_DISCHARGES_SHIPMENT_LOAD_ATTACHMENTS_AttachmentKey",
                        column: x => x.AttachmentKey,
                        principalTable: "SHIPMENT_LOAD_ATTACHMENTS",
                        principalColumn: "Key");
                });

            migrationBuilder.CreateIndex(
                name: "IX_SHIPMENT_LOAD_ATTACHMENTS_ShipmentLoadKey",
                table: "SHIPMENT_LOAD_ATTACHMENTS",
                column: "ShipmentLoadKey");

            migrationBuilder.CreateIndex(
                name: "IX_SHIPMENT_LOAD_DISCHARGES_AttachmentKey",
                table: "SHIPMENT_LOAD_DISCHARGES",
                column: "AttachmentKey");

            migrationBuilder.CreateIndex(
                name: "IX_SHIPMENT_LOAD_DISCHARGES_SalesInvoiceItemKey",
                table: "SHIPMENT_LOAD_DISCHARGES",
                column: "SalesInvoiceItemKey");

            migrationBuilder.CreateIndex(
                name: "IX_SHIPMENT_LOAD_DISCHARGES_SalesInvoiceKey",
                table: "SHIPMENT_LOAD_DISCHARGES",
                column: "SalesInvoiceKey");

            migrationBuilder.CreateIndex(
                name: "IX_SHIPMENT_LOAD_DISCHARGES_ShipmentLoadKey",
                table: "SHIPMENT_LOAD_DISCHARGES",
                column: "ShipmentLoadKey");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SHIPMENT_LOAD_DISCHARGES");

            migrationBuilder.DropTable(
                name: "SHIPMENT_LOAD_ATTACHMENTS");

            migrationBuilder.DropColumn(
                name: "DischargedQuantity",
                table: "SHIPMENT_LOADS");

            migrationBuilder.DropColumn(
                name: "TicketDeliveredQuantity",
                table: "SALES_INVOICES_ITEMS");
        }
    }
}
