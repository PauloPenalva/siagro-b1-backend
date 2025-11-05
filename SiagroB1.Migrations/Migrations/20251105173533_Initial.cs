using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BRANCHS",
                columns: table => new
                {
                    Key = table.Column<string>(type: "VARCHAR(14)", nullable: false),
                    BranchName = table.Column<string>(type: "VARCHAR(100)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BRANCHS", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "HARVEST_SEASSONS",
                columns: table => new
                {
                    BranchKey = table.Column<string>(type: "VARCHAR(14)", nullable: true),
                    Key = table.Column<string>(type: "VARCHAR(10)", nullable: false),
                    Name = table.Column<string>(type: "VARCHAR(100)", nullable: false),
                    Inactive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HARVEST_SEASSONS", x => x.Key);
                    table.ForeignKey(
                        name: "FK_HARVEST_SEASSONS_BRANCHS_BranchKey",
                        column: x => x.BranchKey,
                        principalTable: "BRANCHS",
                        principalColumn: "Key");
                });

            migrationBuilder.CreateTable(
                name: "PROCESSING_COSTS",
                columns: table => new
                {
                    BranchKey = table.Column<string>(type: "VARCHAR(14)", nullable: true),
                    Key = table.Column<string>(type: "VARCHAR(10)", nullable: false),
                    Description = table.Column<string>(type: "VARCHAR(100)", nullable: false),
                    StoragePrice = table.Column<decimal>(type: "DECIMAL(18,8)", nullable: true),
                    StorageGraceDays = table.Column<int>(type: "int", nullable: true),
                    StorageBillingIntervalDays = table.Column<int>(type: "int", nullable: true),
                    FumigationPrice = table.Column<decimal>(type: "DECIMAL(18,8)", nullable: true),
                    FumigationGraceDays = table.Column<int>(type: "int", nullable: true),
                    TechnicalLossGraceDays = table.Column<int>(type: "int", nullable: true),
                    TechnicalLossIntervalDays = table.Column<int>(type: "int", nullable: true),
                    TechnicalLossRate = table.Column<decimal>(type: "DECIMAL(18,8)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PROCESSING_COSTS", x => x.Key);
                    table.ForeignKey(
                        name: "FK_PROCESSING_COSTS_BRANCHS_BranchKey",
                        column: x => x.BranchKey,
                        principalTable: "BRANCHS",
                        principalColumn: "Key");
                });

            migrationBuilder.CreateTable(
                name: "PROCESSING_SERVICES",
                columns: table => new
                {
                    BranchKey = table.Column<string>(type: "VARCHAR(14)", nullable: true),
                    Key = table.Column<string>(type: "VARCHAR(10)", nullable: false),
                    Description = table.Column<string>(type: "VARCHAR(100)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PROCESSING_SERVICES", x => x.Key);
                    table.ForeignKey(
                        name: "FK_PROCESSING_SERVICES_BRANCHS_BranchKey",
                        column: x => x.BranchKey,
                        principalTable: "BRANCHS",
                        principalColumn: "Key");
                });

            migrationBuilder.CreateTable(
                name: "QUALITY_ATTRIBS",
                columns: table => new
                {
                    BranchKey = table.Column<string>(type: "VARCHAR(14)", nullable: true),
                    Key = table.Column<string>(type: "VARCHAR(10)", nullable: false),
                    Name = table.Column<string>(type: "VARCHAR(100)", nullable: false),
                    Disabled = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QUALITY_ATTRIBS", x => x.Key);
                    table.ForeignKey(
                        name: "FK_QUALITY_ATTRIBS_BRANCHS_BranchKey",
                        column: x => x.BranchKey,
                        principalTable: "BRANCHS",
                        principalColumn: "Key");
                });

            migrationBuilder.CreateTable(
                name: "STATES",
                columns: table => new
                {
                    BranchKey = table.Column<string>(type: "VARCHAR(14)", nullable: true),
                    Key = table.Column<string>(type: "VARCHAR(10)", nullable: false),
                    Name = table.Column<string>(type: "VARCHAR(100)", nullable: false),
                    Abbreviation = table.Column<string>(type: "VARCHAR(2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_STATES", x => x.Key);
                    table.ForeignKey(
                        name: "FK_STATES_BRANCHS_BranchKey",
                        column: x => x.BranchKey,
                        principalTable: "BRANCHS",
                        principalColumn: "Key");
                });

            migrationBuilder.CreateTable(
                name: "TRUCK_DRIVERS",
                columns: table => new
                {
                    BranchKey = table.Column<string>(type: "VARCHAR(14)", nullable: true),
                    Key = table.Column<string>(type: "VARCHAR(10)", nullable: false),
                    Name = table.Column<string>(type: "VARCHAR(100)", nullable: false),
                    Cpf = table.Column<string>(type: "VARCHAR(11)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TRUCK_DRIVERS", x => x.Key);
                    table.ForeignKey(
                        name: "FK_TRUCK_DRIVERS_BRANCHS_BranchKey",
                        column: x => x.BranchKey,
                        principalTable: "BRANCHS",
                        principalColumn: "Key");
                });

            migrationBuilder.CreateTable(
                name: "UNITS_OF_MEASURE",
                columns: table => new
                {
                    BranchKey = table.Column<string>(type: "VARCHAR(14)", nullable: true),
                    Key = table.Column<string>(type: "VARCHAR(10)", nullable: false),
                    Description = table.Column<string>(type: "VARCHAR(100)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UNITS_OF_MEASURE", x => x.Key);
                    table.ForeignKey(
                        name: "FK_UNITS_OF_MEASURE_BRANCHS_BranchKey",
                        column: x => x.BranchKey,
                        principalTable: "BRANCHS",
                        principalColumn: "Key");
                });

            migrationBuilder.CreateTable(
                name: "WHAREHOUSE",
                columns: table => new
                {
                    BranchKey = table.Column<string>(type: "VARCHAR(14)", nullable: true),
                    Key = table.Column<string>(type: "VARCHAR(10)", nullable: false),
                    Name = table.Column<string>(type: "VARCHAR(100)", nullable: false),
                    TAXID = table.Column<string>(type: "VARCHAR(14)", nullable: true),
                    Inactive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WHAREHOUSE", x => x.Key);
                    table.ForeignKey(
                        name: "FK_WHAREHOUSE_BRANCHS_BranchKey",
                        column: x => x.BranchKey,
                        principalTable: "BRANCHS",
                        principalColumn: "Key");
                });

            migrationBuilder.CreateTable(
                name: "PROCESSING_COST_DRYING_DETAILS",
                columns: table => new
                {
                    BranchKey = table.Column<string>(type: "VARCHAR(14)", nullable: true),
                    Key = table.Column<string>(type: "VARCHAR(10)", nullable: false),
                    ProcessingCostKey = table.Column<string>(type: "VARCHAR(10)", nullable: false),
                    InitialMoisture = table.Column<decimal>(type: "DECIMAL(18,1)", nullable: true),
                    FinalMoisture = table.Column<decimal>(type: "DECIMAL(18,1)", nullable: true),
                    Price = table.Column<decimal>(type: "DECIMAL(18,8)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PROCESSING_COST_DRYING_DETAILS", x => x.Key);
                    table.ForeignKey(
                        name: "FK_PROCESSING_COST_DRYING_DETAILS_BRANCHS_BranchKey",
                        column: x => x.BranchKey,
                        principalTable: "BRANCHS",
                        principalColumn: "Key");
                    table.ForeignKey(
                        name: "FK_PROCESSING_COST_DRYING_DETAILS_PROCESSING_COSTS_ProcessingCostKey",
                        column: x => x.ProcessingCostKey,
                        principalTable: "PROCESSING_COSTS",
                        principalColumn: "Key",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PROCESSING_COST_DRYING_PARAMETERS",
                columns: table => new
                {
                    BranchKey = table.Column<string>(type: "VARCHAR(14)", nullable: true),
                    Key = table.Column<string>(type: "VARCHAR(10)", nullable: false),
                    ProcessingCostKey = table.Column<string>(type: "VARCHAR(10)", nullable: false),
                    InitialMoisture = table.Column<decimal>(type: "DECIMAL(18,1)", nullable: true),
                    FinalMoisture = table.Column<decimal>(type: "DECIMAL(18,1)", nullable: true),
                    Rate = table.Column<decimal>(type: "DECIMAL(18,1)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PROCESSING_COST_DRYING_PARAMETERS", x => x.Key);
                    table.ForeignKey(
                        name: "FK_PROCESSING_COST_DRYING_PARAMETERS_BRANCHS_BranchKey",
                        column: x => x.BranchKey,
                        principalTable: "BRANCHS",
                        principalColumn: "Key");
                    table.ForeignKey(
                        name: "FK_PROCESSING_COST_DRYING_PARAMETERS_PROCESSING_COSTS_ProcessingCostKey",
                        column: x => x.ProcessingCostKey,
                        principalTable: "PROCESSING_COSTS",
                        principalColumn: "Key",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PROCESSING_COST_SERVICE_DETAILS",
                columns: table => new
                {
                    BranchKey = table.Column<string>(type: "VARCHAR(14)", nullable: true),
                    Key = table.Column<string>(type: "VARCHAR(10)", nullable: false),
                    ProcessingCostKey = table.Column<string>(type: "VARCHAR(10)", nullable: false),
                    ProcessingServiceKey = table.Column<string>(type: "VARCHAR(10)", nullable: true),
                    Price = table.Column<decimal>(type: "DECIMAL(18,8)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PROCESSING_COST_SERVICE_DETAILS", x => x.Key);
                    table.ForeignKey(
                        name: "FK_PROCESSING_COST_SERVICE_DETAILS_BRANCHS_BranchKey",
                        column: x => x.BranchKey,
                        principalTable: "BRANCHS",
                        principalColumn: "Key");
                    table.ForeignKey(
                        name: "FK_PROCESSING_COST_SERVICE_DETAILS_PROCESSING_COSTS_ProcessingCostKey",
                        column: x => x.ProcessingCostKey,
                        principalTable: "PROCESSING_COSTS",
                        principalColumn: "Key",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PROCESSING_COST_SERVICE_DETAILS_PROCESSING_SERVICES_ProcessingServiceKey",
                        column: x => x.ProcessingServiceKey,
                        principalTable: "PROCESSING_SERVICES",
                        principalColumn: "Key");
                });

            migrationBuilder.CreateTable(
                name: "PROCESSING_COST_QUALITY_PARAMETERS",
                columns: table => new
                {
                    BranchKey = table.Column<string>(type: "VARCHAR(14)", nullable: true),
                    Key = table.Column<string>(type: "VARCHAR(10)", nullable: false),
                    ProcessingCostKey = table.Column<string>(type: "VARCHAR(10)", nullable: false),
                    QualityAttribKey = table.Column<string>(type: "VARCHAR(10)", nullable: false),
                    MaxLimitRate = table.Column<decimal>(type: "DECIMAL(18,1)", nullable: true),
                    ExcessDiscountRate = table.Column<decimal>(type: "DECIMAL(18,1)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PROCESSING_COST_QUALITY_PARAMETERS", x => x.Key);
                    table.ForeignKey(
                        name: "FK_PROCESSING_COST_QUALITY_PARAMETERS_BRANCHS_BranchKey",
                        column: x => x.BranchKey,
                        principalTable: "BRANCHS",
                        principalColumn: "Key");
                    table.ForeignKey(
                        name: "FK_PROCESSING_COST_QUALITY_PARAMETERS_PROCESSING_COSTS_ProcessingCostKey",
                        column: x => x.ProcessingCostKey,
                        principalTable: "PROCESSING_COSTS",
                        principalColumn: "Key",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PROCESSING_COST_QUALITY_PARAMETERS_QUALITY_ATTRIBS_QualityAttribKey",
                        column: x => x.QualityAttribKey,
                        principalTable: "QUALITY_ATTRIBS",
                        principalColumn: "Key",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TRUCKS",
                columns: table => new
                {
                    BranchKey = table.Column<string>(type: "VARCHAR(14)", nullable: true),
                    Key = table.Column<string>(type: "VARCHAR(10)", nullable: false),
                    Model = table.Column<string>(type: "VARCHAR(100)", nullable: true),
                    City = table.Column<string>(type: "VARCHAR(100)", nullable: true),
                    StateKey = table.Column<string>(type: "VARCHAR(10)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TRUCKS", x => x.Key);
                    table.ForeignKey(
                        name: "FK_TRUCKS_BRANCHS_BranchKey",
                        column: x => x.BranchKey,
                        principalTable: "BRANCHS",
                        principalColumn: "Key");
                    table.ForeignKey(
                        name: "FK_TRUCKS_STATES_StateKey",
                        column: x => x.StateKey,
                        principalTable: "STATES",
                        principalColumn: "Key");
                });

            migrationBuilder.CreateTable(
                name: "STORAGE_LOTS",
                columns: table => new
                {
                    BranchKey = table.Column<string>(type: "VARCHAR(14)", nullable: true),
                    Key = table.Column<string>(type: "VARCHAR(10)", nullable: false),
                    CreationDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Description = table.Column<string>(type: "VARCHAR(100)", nullable: false),
                    CardCode = table.Column<string>(type: "VARCHAR(10)", nullable: false),
                    ItemCode = table.Column<string>(type: "VARCHAR(10)", nullable: false),
                    ProcessingCostKey = table.Column<string>(type: "VARCHAR(10)", nullable: false),
                    WhareHouseKey = table.Column<string>(type: "VARCHAR(10)", nullable: false),
                    Balance = table.Column<decimal>(type: "DECIMAL(18,3)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_STORAGE_LOTS", x => x.Key);
                    table.ForeignKey(
                        name: "FK_STORAGE_LOTS_BRANCHS_BranchKey",
                        column: x => x.BranchKey,
                        principalTable: "BRANCHS",
                        principalColumn: "Key");
                    table.ForeignKey(
                        name: "FK_STORAGE_LOTS_PROCESSING_COSTS_ProcessingCostKey",
                        column: x => x.ProcessingCostKey,
                        principalTable: "PROCESSING_COSTS",
                        principalColumn: "Key",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_STORAGE_LOTS_WHAREHOUSE_WhareHouseKey",
                        column: x => x.WhareHouseKey,
                        principalTable: "WHAREHOUSE",
                        principalColumn: "Key",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_HARVEST_SEASSONS_BranchKey",
                table: "HARVEST_SEASSONS",
                column: "BranchKey");

            migrationBuilder.CreateIndex(
                name: "IX_PROCESSING_COST_DRYING_DETAILS_BranchKey",
                table: "PROCESSING_COST_DRYING_DETAILS",
                column: "BranchKey");

            migrationBuilder.CreateIndex(
                name: "IX_PROCESSING_COST_DRYING_DETAILS_ProcessingCostKey",
                table: "PROCESSING_COST_DRYING_DETAILS",
                column: "ProcessingCostKey");

            migrationBuilder.CreateIndex(
                name: "IX_PROCESSING_COST_DRYING_PARAMETERS_BranchKey",
                table: "PROCESSING_COST_DRYING_PARAMETERS",
                column: "BranchKey");

            migrationBuilder.CreateIndex(
                name: "IX_PROCESSING_COST_DRYING_PARAMETERS_ProcessingCostKey",
                table: "PROCESSING_COST_DRYING_PARAMETERS",
                column: "ProcessingCostKey");

            migrationBuilder.CreateIndex(
                name: "IX_PROCESSING_COST_QUALITY_PARAMETERS_BranchKey",
                table: "PROCESSING_COST_QUALITY_PARAMETERS",
                column: "BranchKey");

            migrationBuilder.CreateIndex(
                name: "IX_PROCESSING_COST_QUALITY_PARAMETERS_ProcessingCostKey",
                table: "PROCESSING_COST_QUALITY_PARAMETERS",
                column: "ProcessingCostKey");

            migrationBuilder.CreateIndex(
                name: "IX_PROCESSING_COST_QUALITY_PARAMETERS_QualityAttribKey",
                table: "PROCESSING_COST_QUALITY_PARAMETERS",
                column: "QualityAttribKey");

            migrationBuilder.CreateIndex(
                name: "IX_PROCESSING_COST_SERVICE_DETAILS_BranchKey",
                table: "PROCESSING_COST_SERVICE_DETAILS",
                column: "BranchKey");

            migrationBuilder.CreateIndex(
                name: "IX_PROCESSING_COST_SERVICE_DETAILS_ProcessingCostKey",
                table: "PROCESSING_COST_SERVICE_DETAILS",
                column: "ProcessingCostKey");

            migrationBuilder.CreateIndex(
                name: "IX_PROCESSING_COST_SERVICE_DETAILS_ProcessingServiceKey",
                table: "PROCESSING_COST_SERVICE_DETAILS",
                column: "ProcessingServiceKey");

            migrationBuilder.CreateIndex(
                name: "IX_PROCESSING_COSTS_BranchKey",
                table: "PROCESSING_COSTS",
                column: "BranchKey");

            migrationBuilder.CreateIndex(
                name: "IX_PROCESSING_SERVICES_BranchKey",
                table: "PROCESSING_SERVICES",
                column: "BranchKey");

            migrationBuilder.CreateIndex(
                name: "IX_QUALITY_ATTRIBS_BranchKey",
                table: "QUALITY_ATTRIBS",
                column: "BranchKey");

            migrationBuilder.CreateIndex(
                name: "IX_STATES_BranchKey",
                table: "STATES",
                column: "BranchKey");

            migrationBuilder.CreateIndex(
                name: "IX_STORAGE_LOTS_BranchKey",
                table: "STORAGE_LOTS",
                column: "BranchKey");

            migrationBuilder.CreateIndex(
                name: "IX_STORAGE_LOTS_ProcessingCostKey",
                table: "STORAGE_LOTS",
                column: "ProcessingCostKey");

            migrationBuilder.CreateIndex(
                name: "IX_STORAGE_LOTS_WhareHouseKey",
                table: "STORAGE_LOTS",
                column: "WhareHouseKey");

            migrationBuilder.CreateIndex(
                name: "IX_TRUCK_DRIVERS_BranchKey",
                table: "TRUCK_DRIVERS",
                column: "BranchKey");

            migrationBuilder.CreateIndex(
                name: "IX_TRUCKS_BranchKey",
                table: "TRUCKS",
                column: "BranchKey");

            migrationBuilder.CreateIndex(
                name: "IX_TRUCKS_StateKey",
                table: "TRUCKS",
                column: "StateKey");

            migrationBuilder.CreateIndex(
                name: "IX_UNITS_OF_MEASURE_BranchKey",
                table: "UNITS_OF_MEASURE",
                column: "BranchKey");

            migrationBuilder.CreateIndex(
                name: "IX_WHAREHOUSE_BranchKey",
                table: "WHAREHOUSE",
                column: "BranchKey");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HARVEST_SEASSONS");

            migrationBuilder.DropTable(
                name: "PROCESSING_COST_DRYING_DETAILS");

            migrationBuilder.DropTable(
                name: "PROCESSING_COST_DRYING_PARAMETERS");

            migrationBuilder.DropTable(
                name: "PROCESSING_COST_QUALITY_PARAMETERS");

            migrationBuilder.DropTable(
                name: "PROCESSING_COST_SERVICE_DETAILS");

            migrationBuilder.DropTable(
                name: "STORAGE_LOTS");

            migrationBuilder.DropTable(
                name: "TRUCK_DRIVERS");

            migrationBuilder.DropTable(
                name: "TRUCKS");

            migrationBuilder.DropTable(
                name: "UNITS_OF_MEASURE");

            migrationBuilder.DropTable(
                name: "QUALITY_ATTRIBS");

            migrationBuilder.DropTable(
                name: "PROCESSING_SERVICES");

            migrationBuilder.DropTable(
                name: "PROCESSING_COSTS");

            migrationBuilder.DropTable(
                name: "WHAREHOUSE");

            migrationBuilder.DropTable(
                name: "STATES");

            migrationBuilder.DropTable(
                name: "BRANCHS");
        }
    }
}
