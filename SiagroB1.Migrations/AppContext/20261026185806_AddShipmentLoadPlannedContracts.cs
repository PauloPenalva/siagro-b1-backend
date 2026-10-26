using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class AddShipmentLoadPlannedContracts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SHIPMENT_LOADS_PLANNED_PURCHASE_CONTRACTS",
                columns: table => new
                {
                    Key = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ShipmentLoadKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContractKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlannedQuantity = table.Column<decimal>(type: "DECIMAL(18,3)", nullable: true),
                    Notes = table.Column<string>(type: "VARCHAR(200)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "VARCHAR(100)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SHIPMENT_LOADS_PLANNED_PURCHASE_CONTRACTS", x => x.Key);
                    table.ForeignKey(
                        name: "FK_SHIPMENT_LOADS_PLANNED_PURCHASE_CONTRACTS_PURCHASE_CONTRACTS_ContractKey",
                        column: x => x.ContractKey,
                        principalTable: "PURCHASE_CONTRACTS",
                        principalColumn: "Key");
                    table.ForeignKey(
                        name: "FK_SHIPMENT_LOADS_PLANNED_PURCHASE_CONTRACTS_SHIPMENT_LOADS_ShipmentLoadKey",
                        column: x => x.ShipmentLoadKey,
                        principalTable: "SHIPMENT_LOADS",
                        principalColumn: "Key");
                });

            migrationBuilder.CreateTable(
                name: "SHIPMENT_LOADS_PLANNED_SALES_CONTRACTS",
                columns: table => new
                {
                    Key = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ShipmentLoadKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ContractKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PlannedQuantity = table.Column<decimal>(type: "DECIMAL(18,3)", nullable: true),
                    Notes = table.Column<string>(type: "VARCHAR(200)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "VARCHAR(100)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SHIPMENT_LOADS_PLANNED_SALES_CONTRACTS", x => x.Key);
                    table.ForeignKey(
                        name: "FK_SHIPMENT_LOADS_PLANNED_SALES_CONTRACTS_SALES_CONTRACTS_ContractKey",
                        column: x => x.ContractKey,
                        principalTable: "SALES_CONTRACTS",
                        principalColumn: "Key");
                    table.ForeignKey(
                        name: "FK_SHIPMENT_LOADS_PLANNED_SALES_CONTRACTS_SHIPMENT_LOADS_ShipmentLoadKey",
                        column: x => x.ShipmentLoadKey,
                        principalTable: "SHIPMENT_LOADS",
                        principalColumn: "Key");
                });

            migrationBuilder.CreateIndex(
                name: "IX_SHIPMENT_LOADS_PLANNED_PURCHASE_CONTRACTS_ContractKey",
                table: "SHIPMENT_LOADS_PLANNED_PURCHASE_CONTRACTS",
                column: "ContractKey");

            migrationBuilder.CreateIndex(
                name: "IX_SHIPMENT_LOADS_PLANNED_PURCHASE_CONTRACTS_ShipmentLoadKey_ContractKey",
                table: "SHIPMENT_LOADS_PLANNED_PURCHASE_CONTRACTS",
                columns: new[] { "ShipmentLoadKey", "ContractKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SHIPMENT_LOADS_PLANNED_SALES_CONTRACTS_ContractKey",
                table: "SHIPMENT_LOADS_PLANNED_SALES_CONTRACTS",
                column: "ContractKey");

            migrationBuilder.CreateIndex(
                name: "IX_SHIPMENT_LOADS_PLANNED_SALES_CONTRACTS_ShipmentLoadKey_ContractKey",
                table: "SHIPMENT_LOADS_PLANNED_SALES_CONTRACTS",
                columns: new[] { "ShipmentLoadKey", "ContractKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SHIPMENT_LOADS_PLANNED_PURCHASE_CONTRACTS");

            migrationBuilder.DropTable(
                name: "SHIPMENT_LOADS_PLANNED_SALES_CONTRACTS");
        }
    }
}
