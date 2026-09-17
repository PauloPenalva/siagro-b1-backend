using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class AddWarehouseReconciliationReleases : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WAREHOUSE_RECONCILIATION_RELEASES",
                columns: table => new
                {
                    Key = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WarehouseReconciliationKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ShipmentReleaseKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Quantity = table.Column<decimal>(type: "DECIMAL(18,3)", nullable: false),
                    PurchaseStorageTransactionKey = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    LossStorageTransactionKey = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WAREHOUSE_RECONCILIATION_RELEASES", x => x.Key);
                    table.ForeignKey(
                        name: "FK_WAREHOUSE_RECONCILIATION_RELEASES_SHIPMENT_RELEASES_ShipmentReleaseKey",
                        column: x => x.ShipmentReleaseKey,
                        principalTable: "SHIPMENT_RELEASES",
                        principalColumn: "Key",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WAREHOUSE_RECONCILIATION_RELEASES_WAREHOUSE_RECONCILIATIONS_WarehouseReconciliationKey",
                        column: x => x.WarehouseReconciliationKey,
                        principalTable: "WAREHOUSE_RECONCILIATIONS",
                        principalColumn: "Key",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WAREHOUSE_RECONCILIATION_RELEASES_ShipmentReleaseKey",
                table: "WAREHOUSE_RECONCILIATION_RELEASES",
                column: "ShipmentReleaseKey");

            migrationBuilder.CreateIndex(
                name: "IX_WAREHOUSE_RECONCILIATION_RELEASES_WarehouseReconciliationKey_ShipmentReleaseKey",
                table: "WAREHOUSE_RECONCILIATION_RELEASES",
                columns: new[] { "WarehouseReconciliationKey", "ShipmentReleaseKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WAREHOUSE_RECONCILIATION_RELEASES");
        }
    }
}
