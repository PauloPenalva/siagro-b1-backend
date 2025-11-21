using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class CreateTableLogisticRegions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LogisticRegionCode",
                table: "PURCHASE_CONTRACTS",
                type: "VARCHAR(10)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "LOGISTIC_REGIONS",
                columns: table => new
                {
                    Code = table.Column<string>(type: "VARCHAR(10)", nullable: false),
                    Name = table.Column<string>(type: "VARCHAR(100)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LOGISTIC_REGIONS", x => x.Code);
                });

            migrationBuilder.CreateTable(
                name: "SALES_CONTRACTS",
                columns: table => new
                {
                    BranchCode = table.Column<string>(type: "VARCHAR(14)", nullable: false),
                    Key = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "VARCHAR(50)", nullable: false),
                    Sequence = table.Column<string>(type: "VARCHAR(3)", nullable: false),
                    Complement = table.Column<string>(type: "VARCHAR(100)", nullable: true),
                    CreationDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Type = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: true),
                    CardCode = table.Column<string>(type: "VARCHAR(10)", nullable: false),
                    DeliveryStartDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DeliveryEndDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FreightTerms = table.Column<int>(type: "int", nullable: false),
                    FreightCost = table.Column<decimal>(type: "DECIMAL(18,2)", nullable: false),
                    ItemCode = table.Column<string>(type: "VARCHAR(10)", nullable: false),
                    UnitOfMeasureCode = table.Column<string>(type: "VARCHAR(4)", nullable: false),
                    HarvestSeasonCode = table.Column<string>(type: "VARCHAR(10)", nullable: false),
                    TotalVolume = table.Column<decimal>(type: "DECIMAL(18,3)", nullable: false),
                    DeliveryLocationCode = table.Column<string>(type: "VARCHAR(10)", nullable: false),
                    Comments = table.Column<string>(type: "VARCHAR(500)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<string>(type: "VARCHAR(100)", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<string>(type: "VARCHAR(100)", nullable: true),
                    ApprovedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ApprovedBy = table.Column<string>(type: "VARCHAR(100)", nullable: true),
                    CanceledAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CanceledBy = table.Column<string>(type: "VARCHAR(100)", nullable: true),
                    LogisticRegionCode = table.Column<string>(type: "VARCHAR(10)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SALES_CONTRACTS", x => x.Key);
                    table.ForeignKey(
                        name: "FK_SALES_CONTRACTS_HARVEST_SEASSONS_HarvestSeasonCode",
                        column: x => x.HarvestSeasonCode,
                        principalTable: "HARVEST_SEASSONS",
                        principalColumn: "Code",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SALES_CONTRACTS_LOGISTIC_REGIONS_LogisticRegionCode",
                        column: x => x.LogisticRegionCode,
                        principalTable: "LOGISTIC_REGIONS",
                        principalColumn: "Code");
                    table.ForeignKey(
                        name: "FK_SALES_CONTRACTS_UNITS_OF_MEASURE_UnitOfMeasureCode",
                        column: x => x.UnitOfMeasureCode,
                        principalTable: "UNITS_OF_MEASURE",
                        principalColumn: "Code",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SALES_CONTRACTS_WAREHOUSES_DeliveryLocationCode",
                        column: x => x.DeliveryLocationCode,
                        principalTable: "WAREHOUSES",
                        principalColumn: "Code",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SALES_CONTRACTS_PRICE_FIXATIONS",
                columns: table => new
                {
                    Key = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SalesContractKey = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    FixationDate = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FixationVolume = table.Column<decimal>(type: "DECIMAL(18,3)", nullable: false),
                    FixationPrice = table.Column<decimal>(type: "DECIMAL(18,8)", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SALES_CONTRACTS_PRICE_FIXATIONS", x => x.Key);
                    table.ForeignKey(
                        name: "FK_SALES_CONTRACTS_PRICE_FIXATIONS_SALES_CONTRACTS_SalesContractKey",
                        column: x => x.SalesContractKey,
                        principalTable: "SALES_CONTRACTS",
                        principalColumn: "Key");
                });

            migrationBuilder.CreateIndex(
                name: "IX_PURCHASE_CONTRACTS_LogisticRegionCode",
                table: "PURCHASE_CONTRACTS",
                column: "LogisticRegionCode");

            migrationBuilder.CreateIndex(
                name: "IX_SALES_CONTRACTS_DeliveryLocationCode",
                table: "SALES_CONTRACTS",
                column: "DeliveryLocationCode");

            migrationBuilder.CreateIndex(
                name: "IX_SALES_CONTRACTS_HarvestSeasonCode",
                table: "SALES_CONTRACTS",
                column: "HarvestSeasonCode");

            migrationBuilder.CreateIndex(
                name: "IX_SALES_CONTRACTS_LogisticRegionCode",
                table: "SALES_CONTRACTS",
                column: "LogisticRegionCode");

            migrationBuilder.CreateIndex(
                name: "IX_SALES_CONTRACTS_UnitOfMeasureCode",
                table: "SALES_CONTRACTS",
                column: "UnitOfMeasureCode");

            migrationBuilder.CreateIndex(
                name: "IX_SALES_CONTRACTS_PRICE_FIXATIONS_SalesContractKey",
                table: "SALES_CONTRACTS_PRICE_FIXATIONS",
                column: "SalesContractKey");

            migrationBuilder.AddForeignKey(
                name: "FK_PURCHASE_CONTRACTS_LOGISTIC_REGIONS_LogisticRegionCode",
                table: "PURCHASE_CONTRACTS",
                column: "LogisticRegionCode",
                principalTable: "LOGISTIC_REGIONS",
                principalColumn: "Code");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PURCHASE_CONTRACTS_LOGISTIC_REGIONS_LogisticRegionCode",
                table: "PURCHASE_CONTRACTS");

            migrationBuilder.DropTable(
                name: "SALES_CONTRACTS_PRICE_FIXATIONS");

            migrationBuilder.DropTable(
                name: "SALES_CONTRACTS");

            migrationBuilder.DropTable(
                name: "LOGISTIC_REGIONS");

            migrationBuilder.DropIndex(
                name: "IX_PURCHASE_CONTRACTS_LogisticRegionCode",
                table: "PURCHASE_CONTRACTS");

            migrationBuilder.DropColumn(
                name: "LogisticRegionCode",
                table: "PURCHASE_CONTRACTS");
        }
    }
}
