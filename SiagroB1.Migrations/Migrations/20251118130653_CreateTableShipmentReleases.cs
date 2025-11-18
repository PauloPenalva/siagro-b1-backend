using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class CreateTableShipmentReleases : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SHIPMENT_RELEASES",
                columns: table => new
                {
                    BranchCode = table.Column<string>(type: "VARCHAR(14)", nullable: false),
                    Key = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PurchaseContractKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ReleaseNumber = table.Column<string>(type: "VARCHAR(15)", nullable: false),
                    ReleaseDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpectedDeliveryDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReleasedQuantity = table.Column<decimal>(type: "DECIMAL(18,3)", nullable: false),
                    AvailableQuantity = table.Column<decimal>(type: "DECIMAL(18,3)", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CreatedBy = table.Column<string>(type: "VARCHAR(50)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ApprovedBy = table.Column<string>(type: "VARCHAR(50)", nullable: true),
                    ApprovedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SHIPMENT_RELEASES", x => x.Key);
                    table.ForeignKey(
                        name: "FK_SHIPMENT_RELEASES_BRANCHS_BranchCode",
                        column: x => x.BranchCode,
                        principalTable: "BRANCHS",
                        principalColumn: "Code",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SHIPPING_MANIFESTS",
                columns: table => new
                {
                    BranchCode = table.Column<string>(type: "VARCHAR(14)", nullable: false),
                    Key = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ManifestNumber = table.Column<string>(type: "VARCHAR(15)", nullable: false),
                    ManifestDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ShipmentReleaseKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    GrossWeight = table.Column<decimal>(type: "DECIMAL(18,3)", nullable: false),
                    TareWeight = table.Column<decimal>(type: "DECIMAL(18,3)", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    CreatedBy = table.Column<string>(type: "VARCHAR(100)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SHIPPING_MANIFESTS", x => x.Key);
                    table.ForeignKey(
                        name: "FK_SHIPPING_MANIFESTS_BRANCHS_BranchCode",
                        column: x => x.BranchCode,
                        principalTable: "BRANCHS",
                        principalColumn: "Code",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SHIPMENT_RELEASES_BranchCode",
                table: "SHIPMENT_RELEASES",
                column: "BranchCode");

            migrationBuilder.CreateIndex(
                name: "IX_SHIPMENT_RELEASES_ReleaseNumber",
                table: "SHIPMENT_RELEASES",
                column: "ReleaseNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SHIPPING_MANIFESTS_BranchCode",
                table: "SHIPPING_MANIFESTS",
                column: "BranchCode");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SHIPMENT_RELEASES");

            migrationBuilder.DropTable(
                name: "SHIPPING_MANIFESTS");
        }
    }
}
