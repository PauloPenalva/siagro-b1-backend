using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class DropTableStorageLot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "STORAGE_LOTS");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "STORAGE_LOTS",
                columns: table => new
                {
                    BranchCode = table.Column<string>(type: "VARCHAR(14)", nullable: false),
                    Key = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProcessingCostCode = table.Column<string>(type: "VARCHAR(10)", nullable: false),
                    WarehouseCode = table.Column<string>(type: "VARCHAR(10)", nullable: false),
                    Balance = table.Column<decimal>(type: "DECIMAL(18,3)", nullable: false),
                    CardCode = table.Column<string>(type: "VARCHAR(10)", nullable: false),
                    CreationDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Description = table.Column<string>(type: "VARCHAR(100)", nullable: false),
                    ItemCode = table.Column<string>(type: "VARCHAR(10)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_STORAGE_LOTS", x => x.Key);
                    table.ForeignKey(
                        name: "FK_STORAGE_LOTS_PROCESSING_COSTS_ProcessingCostCode",
                        column: x => x.ProcessingCostCode,
                        principalTable: "PROCESSING_COSTS",
                        principalColumn: "Code",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_STORAGE_LOTS_WAREHOUSES_WarehouseCode",
                        column: x => x.WarehouseCode,
                        principalTable: "WAREHOUSES",
                        principalColumn: "Code",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_STORAGE_LOTS_ProcessingCostCode",
                table: "STORAGE_LOTS",
                column: "ProcessingCostCode");

            migrationBuilder.CreateIndex(
                name: "IX_STORAGE_LOTS_WarehouseCode",
                table: "STORAGE_LOTS",
                column: "WarehouseCode");
        }
    }
}
