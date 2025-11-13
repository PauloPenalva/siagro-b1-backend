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
                name: "STATES",
                columns: table => new
                {
                    Key = table.Column<string>(type: "VARCHAR(2)", nullable: false),
                    Name = table.Column<string>(type: "VARCHAR(100)", nullable: false),
                    Abbreviation = table.Column<string>(type: "VARCHAR(2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_STATES", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "UNITS_OF_MEASURE",
                columns: table => new
                {
                    Key = table.Column<string>(type: "VARCHAR(4)", nullable: false),
                    Description = table.Column<string>(type: "VARCHAR(100)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UNITS_OF_MEASURE", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "HARVEST_SEASSONS",
                columns: table => new
                {
                    BranchKey = table.Column<string>(type: "VARCHAR(14)", nullable: true),
                    Key = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
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
                    Key = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Description = table.Column<string>(type: "VARCHAR(100)", nullable: false),
                    StoragePrice = table.Column<decimal>(type: "DECIMAL(18,8)", nullable: true),
                    StorageGraceDays = table.Column<int>(type: "int", nullable: true),
                    StorageBillingIntervalDays = table.Column<int>(type: "int", nullable: true),
                    FumigationPrice = table.Column<decimal>(type: "DECIMAL(18,8)", nullable: true),
                    FumigationIntervalDays = table.Column<int>(type: "int", nullable: true),
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
                    Key = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
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
                    Key = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
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
                name: "TRUCK_DRIVERS",
                columns: table => new
                {
                    BranchKey = table.Column<string>(type: "VARCHAR(14)", nullable: true),
                    Key = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
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
                name: "WAREHOUSES",
                columns: table => new
                {
                    BranchKey = table.Column<string>(type: "VARCHAR(14)", nullable: true),
                    Key = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "VARCHAR(100)", nullable: false),
                    TAXID = table.Column<string>(type: "VARCHAR(14)", nullable: true),
                    Inactive = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WAREHOUSES", x => x.Key);
                    table.ForeignKey(
                        name: "FK_WAREHOUSES_BRANCHS_BranchKey",
                        column: x => x.BranchKey,
                        principalTable: "BRANCHS",
                        principalColumn: "Key");
                });

            migrationBuilder.CreateTable(
                name: "TRUCKS",
                columns: table => new
                {
                    BranchKey = table.Column<string>(type: "VARCHAR(14)", nullable: true),
                    Key = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VARCHAR7NOTNULL = table.Column<string>(name: "VARCHAR(7) NOT NULL", type: "nvarchar(max)", nullable: false),
                    Model = table.Column<string>(type: "VARCHAR(100)", nullable: true),
                    City = table.Column<string>(type: "VARCHAR(100)", nullable: true),
                    StateKey = table.Column<string>(type: "VARCHAR(2)", nullable: true)
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
                name: "PROCESSING_COST_DRYING_DETAILS",
                columns: table => new
                {
                    Key = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProcessingCostKey = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    InitialMoisture = table.Column<decimal>(type: "DECIMAL(18,1)", nullable: true),
                    FinalMoisture = table.Column<decimal>(type: "DECIMAL(18,1)", nullable: true),
                    Price = table.Column<decimal>(type: "DECIMAL(18,8)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PROCESSING_COST_DRYING_DETAILS", x => x.Key);
                    table.ForeignKey(
                        name: "FK_PROCESSING_COST_DRYING_DETAILS_PROCESSING_COSTS_ProcessingCostKey",
                        column: x => x.ProcessingCostKey,
                        principalTable: "PROCESSING_COSTS",
                        principalColumn: "Key");
                });

            migrationBuilder.CreateTable(
                name: "PROCESSING_COST_DRYING_PARAMETERS",
                columns: table => new
                {
                    Key = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProcessingCostKey = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    InitialMoisture = table.Column<decimal>(type: "DECIMAL(18,1)", nullable: true),
                    FinalMoisture = table.Column<decimal>(type: "DECIMAL(18,1)", nullable: true),
                    Rate = table.Column<decimal>(type: "DECIMAL(18,8)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PROCESSING_COST_DRYING_PARAMETERS", x => x.Key);
                    table.ForeignKey(
                        name: "FK_PROCESSING_COST_DRYING_PARAMETERS_PROCESSING_COSTS_ProcessingCostKey",
                        column: x => x.ProcessingCostKey,
                        principalTable: "PROCESSING_COSTS",
                        principalColumn: "Key");
                });

            migrationBuilder.CreateTable(
                name: "PROCESSING_COST_SERVICE_DETAILS",
                columns: table => new
                {
                    Key = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProcessingCostKey = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ProcessingServiceKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Price = table.Column<decimal>(type: "DECIMAL(18,8)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PROCESSING_COST_SERVICE_DETAILS", x => x.Key);
                    table.ForeignKey(
                        name: "FK_PROCESSING_COST_SERVICE_DETAILS_PROCESSING_COSTS_ProcessingCostKey",
                        column: x => x.ProcessingCostKey,
                        principalTable: "PROCESSING_COSTS",
                        principalColumn: "Key");
                    table.ForeignKey(
                        name: "FK_PROCESSING_COST_SERVICE_DETAILS_PROCESSING_SERVICES_ProcessingServiceKey",
                        column: x => x.ProcessingServiceKey,
                        principalTable: "PROCESSING_SERVICES",
                        principalColumn: "Key",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PROCESSING_COST_QUALITY_PARAMETERS",
                columns: table => new
                {
                    Key = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProcessingCostKey = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    QualityAttribKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    MaxLimitRate = table.Column<decimal>(type: "DECIMAL(18,1)", nullable: true),
                    ExcessDiscountRate = table.Column<decimal>(type: "DECIMAL(18,1)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PROCESSING_COST_QUALITY_PARAMETERS", x => x.Key);
                    table.ForeignKey(
                        name: "FK_PROCESSING_COST_QUALITY_PARAMETERS_PROCESSING_COSTS_ProcessingCostKey",
                        column: x => x.ProcessingCostKey,
                        principalTable: "PROCESSING_COSTS",
                        principalColumn: "Key");
                    table.ForeignKey(
                        name: "FK_PROCESSING_COST_QUALITY_PARAMETERS_QUALITY_ATTRIBS_QualityAttribKey",
                        column: x => x.QualityAttribKey,
                        principalTable: "QUALITY_ATTRIBS",
                        principalColumn: "Key",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PURCHASE_CONTRACTS",
                columns: table => new
                {
                    BranchKey = table.Column<string>(type: "VARCHAR(14)", nullable: true),
                    Key = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Complement = table.Column<string>(type: "VARCHAR(50)", nullable: true),
                    CreationDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    BusinessParterKey = table.Column<string>(type: "VARCHAR(10)", nullable: false),
                    DeliveryStartDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    DeliveryEndDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FreightTerms = table.Column<int>(type: "int", nullable: false),
                    FreightCost = table.Column<decimal>(type: "DECIMAL(18,2)", nullable: false),
                    ProductKey = table.Column<string>(type: "VARCHAR(10)", nullable: false),
                    UnitOfMeasureKey = table.Column<string>(type: "VARCHAR(4)", nullable: false),
                    HarvestSeasonKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TotalVolume = table.Column<decimal>(type: "DECIMAL(18,3)", nullable: false),
                    DeliveryLocationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Comments = table.Column<string>(type: "NVARCHAR(MAX)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PURCHASE_CONTRACTS", x => x.Key);
                    table.ForeignKey(
                        name: "FK_PURCHASE_CONTRACTS_BRANCHS_BranchKey",
                        column: x => x.BranchKey,
                        principalTable: "BRANCHS",
                        principalColumn: "Key");
                    table.ForeignKey(
                        name: "FK_PURCHASE_CONTRACTS_HARVEST_SEASSONS_HarvestSeasonKey",
                        column: x => x.HarvestSeasonKey,
                        principalTable: "HARVEST_SEASSONS",
                        principalColumn: "Key",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PURCHASE_CONTRACTS_UNITS_OF_MEASURE_UnitOfMeasureKey",
                        column: x => x.UnitOfMeasureKey,
                        principalTable: "UNITS_OF_MEASURE",
                        principalColumn: "Key",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PURCHASE_CONTRACTS_WAREHOUSES_DeliveryLocationId",
                        column: x => x.DeliveryLocationId,
                        principalTable: "WAREHOUSES",
                        principalColumn: "Key",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "STORAGE_LOTS",
                columns: table => new
                {
                    BranchKey = table.Column<string>(type: "VARCHAR(14)", nullable: true),
                    Key = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CreationDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Description = table.Column<string>(type: "VARCHAR(100)", nullable: false),
                    CardCode = table.Column<string>(type: "VARCHAR(10)", nullable: false),
                    ItemCode = table.Column<string>(type: "VARCHAR(10)", nullable: false),
                    ProcessingCostKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WarehouseKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
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
                        name: "FK_STORAGE_LOTS_WAREHOUSES_WarehouseKey",
                        column: x => x.WarehouseKey,
                        principalTable: "WAREHOUSES",
                        principalColumn: "Key",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WEIGHTING_TICKETS",
                columns: table => new
                {
                    BranchKey = table.Column<string>(type: "VARCHAR(14)", nullable: true),
                    Key = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Date = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Time = table.Column<int>(type: "int", nullable: false),
                    Operation = table.Column<string>(type: "VARCHAR(50)", maxLength: 50, nullable: false),
                    ItemCode = table.Column<string>(type: "VARCHAR(10)", maxLength: 10, nullable: false),
                    CardCode = table.Column<string>(type: "VARCHAR(10)", maxLength: 10, nullable: false),
                    TruckKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    TruckDriverKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FirstWeighValue = table.Column<int>(type: "int", nullable: false),
                    FirstWeighDateTime = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    SecondWeighValue = table.Column<int>(type: "int", nullable: false),
                    SecondWeighDateTime = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    GrossWeight = table.Column<int>(type: "int", nullable: false),
                    Comments = table.Column<string>(type: "NVARCHAR(MAX)", nullable: true),
                    ProcessigCostKey = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WEIGHTING_TICKETS", x => x.Key);
                    table.ForeignKey(
                        name: "FK_WEIGHTING_TICKETS_BRANCHS_BranchKey",
                        column: x => x.BranchKey,
                        principalTable: "BRANCHS",
                        principalColumn: "Key");
                    table.ForeignKey(
                        name: "FK_WEIGHTING_TICKETS_PROCESSING_COSTS_ProcessigCostKey",
                        column: x => x.ProcessigCostKey,
                        principalTable: "PROCESSING_COSTS",
                        principalColumn: "Key");
                    table.ForeignKey(
                        name: "FK_WEIGHTING_TICKETS_TRUCKS_TruckKey",
                        column: x => x.TruckKey,
                        principalTable: "TRUCKS",
                        principalColumn: "Key",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_WEIGHTING_TICKETS_TRUCK_DRIVERS_TruckDriverKey",
                        column: x => x.TruckDriverKey,
                        principalTable: "TRUCK_DRIVERS",
                        principalColumn: "Key",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PURCHASE_CONTRACTS_PRICE_FIXATIONS",
                columns: table => new
                {
                    Key = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PurchaseContractKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FixationDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FixationVolume = table.Column<decimal>(type: "DECIMAL(18,3)", nullable: false),
                    FixationPrice = table.Column<decimal>(type: "DECIMAL(18,8)", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PURCHASE_CONTRACTS_PRICE_FIXATIONS", x => x.Key);
                    table.ForeignKey(
                        name: "FK_PURCHASE_CONTRACTS_PRICE_FIXATIONS_PURCHASE_CONTRACTS_PurchaseContractKey",
                        column: x => x.PurchaseContractKey,
                        principalTable: "PURCHASE_CONTRACTS",
                        principalColumn: "Key",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "QUALITY_INSPECTIONS",
                columns: table => new
                {
                    Key = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WeighingTicketKey = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    QualityAttribKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Value = table.Column<decimal>(type: "DECIMAL(18,1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QUALITY_INSPECTIONS", x => x.Key);
                    table.ForeignKey(
                        name: "FK_QUALITY_INSPECTIONS_QUALITY_ATTRIBS_QualityAttribKey",
                        column: x => x.QualityAttribKey,
                        principalTable: "QUALITY_ATTRIBS",
                        principalColumn: "Key",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_QUALITY_INSPECTIONS_WEIGHTING_TICKETS_WeighingTicketKey",
                        column: x => x.WeighingTicketKey,
                        principalTable: "WEIGHTING_TICKETS",
                        principalColumn: "Key");
                });

            migrationBuilder.CreateIndex(
                name: "IX_HARVEST_SEASSONS_BranchKey",
                table: "HARVEST_SEASSONS",
                column: "BranchKey");

            migrationBuilder.CreateIndex(
                name: "IX_PROCESSING_COST_DRYING_DETAILS_ProcessingCostKey",
                table: "PROCESSING_COST_DRYING_DETAILS",
                column: "ProcessingCostKey");

            migrationBuilder.CreateIndex(
                name: "IX_PROCESSING_COST_DRYING_PARAMETERS_ProcessingCostKey",
                table: "PROCESSING_COST_DRYING_PARAMETERS",
                column: "ProcessingCostKey");

            migrationBuilder.CreateIndex(
                name: "IX_PROCESSING_COST_QUALITY_PARAMETERS_ProcessingCostKey",
                table: "PROCESSING_COST_QUALITY_PARAMETERS",
                column: "ProcessingCostKey");

            migrationBuilder.CreateIndex(
                name: "IX_PROCESSING_COST_QUALITY_PARAMETERS_QualityAttribKey",
                table: "PROCESSING_COST_QUALITY_PARAMETERS",
                column: "QualityAttribKey");

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
                name: "IX_PURCHASE_CONTRACTS_BranchKey",
                table: "PURCHASE_CONTRACTS",
                column: "BranchKey");

            migrationBuilder.CreateIndex(
                name: "IX_PURCHASE_CONTRACTS_DeliveryLocationId",
                table: "PURCHASE_CONTRACTS",
                column: "DeliveryLocationId");

            migrationBuilder.CreateIndex(
                name: "IX_PURCHASE_CONTRACTS_HarvestSeasonKey",
                table: "PURCHASE_CONTRACTS",
                column: "HarvestSeasonKey");

            migrationBuilder.CreateIndex(
                name: "IX_PURCHASE_CONTRACTS_UnitOfMeasureKey",
                table: "PURCHASE_CONTRACTS",
                column: "UnitOfMeasureKey");

            migrationBuilder.CreateIndex(
                name: "IX_PURCHASE_CONTRACTS_PRICE_FIXATIONS_PurchaseContractKey",
                table: "PURCHASE_CONTRACTS_PRICE_FIXATIONS",
                column: "PurchaseContractKey");

            migrationBuilder.CreateIndex(
                name: "IX_QUALITY_ATTRIBS_BranchKey",
                table: "QUALITY_ATTRIBS",
                column: "BranchKey");

            migrationBuilder.CreateIndex(
                name: "IX_QUALITY_INSPECTIONS_QualityAttribKey",
                table: "QUALITY_INSPECTIONS",
                column: "QualityAttribKey");

            migrationBuilder.CreateIndex(
                name: "IX_QUALITY_INSPECTIONS_WeighingTicketKey",
                table: "QUALITY_INSPECTIONS",
                column: "WeighingTicketKey");

            migrationBuilder.CreateIndex(
                name: "IX_STORAGE_LOTS_BranchKey",
                table: "STORAGE_LOTS",
                column: "BranchKey");

            migrationBuilder.CreateIndex(
                name: "IX_STORAGE_LOTS_ProcessingCostKey",
                table: "STORAGE_LOTS",
                column: "ProcessingCostKey");

            migrationBuilder.CreateIndex(
                name: "IX_STORAGE_LOTS_WarehouseKey",
                table: "STORAGE_LOTS",
                column: "WarehouseKey");

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
                name: "IX_WAREHOUSES_BranchKey",
                table: "WAREHOUSES",
                column: "BranchKey");

            migrationBuilder.CreateIndex(
                name: "IX_WEIGHTING_TICKETS_BranchKey",
                table: "WEIGHTING_TICKETS",
                column: "BranchKey");

            migrationBuilder.CreateIndex(
                name: "IX_WEIGHTING_TICKETS_ProcessigCostKey",
                table: "WEIGHTING_TICKETS",
                column: "ProcessigCostKey");

            migrationBuilder.CreateIndex(
                name: "IX_WEIGHTING_TICKETS_TruckDriverKey",
                table: "WEIGHTING_TICKETS",
                column: "TruckDriverKey");

            migrationBuilder.CreateIndex(
                name: "IX_WEIGHTING_TICKETS_TruckKey",
                table: "WEIGHTING_TICKETS",
                column: "TruckKey");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PROCESSING_COST_DRYING_DETAILS");

            migrationBuilder.DropTable(
                name: "PROCESSING_COST_DRYING_PARAMETERS");

            migrationBuilder.DropTable(
                name: "PROCESSING_COST_QUALITY_PARAMETERS");

            migrationBuilder.DropTable(
                name: "PROCESSING_COST_SERVICE_DETAILS");

            migrationBuilder.DropTable(
                name: "PURCHASE_CONTRACTS_PRICE_FIXATIONS");

            migrationBuilder.DropTable(
                name: "QUALITY_INSPECTIONS");

            migrationBuilder.DropTable(
                name: "STORAGE_LOTS");

            migrationBuilder.DropTable(
                name: "PROCESSING_SERVICES");

            migrationBuilder.DropTable(
                name: "PURCHASE_CONTRACTS");

            migrationBuilder.DropTable(
                name: "QUALITY_ATTRIBS");

            migrationBuilder.DropTable(
                name: "WEIGHTING_TICKETS");

            migrationBuilder.DropTable(
                name: "HARVEST_SEASSONS");

            migrationBuilder.DropTable(
                name: "UNITS_OF_MEASURE");

            migrationBuilder.DropTable(
                name: "WAREHOUSES");

            migrationBuilder.DropTable(
                name: "PROCESSING_COSTS");

            migrationBuilder.DropTable(
                name: "TRUCKS");

            migrationBuilder.DropTable(
                name: "TRUCK_DRIVERS");

            migrationBuilder.DropTable(
                name: "STATES");

            migrationBuilder.DropTable(
                name: "BRANCHS");
        }
    }
}
