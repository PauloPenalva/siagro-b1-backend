using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class AddFiscalStockAdjustmentsAndPeriods : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FISCAL_STOCK_ADJUSTMENTS",
                columns: table => new
                {
                    Key = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BranchCode = table.Column<string>(type: "VARCHAR(10)", nullable: false),
                    DocNumber = table.Column<string>(type: "VARCHAR(20)", nullable: false),
                    AdjustmentDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ItemCode = table.Column<string>(type: "VARCHAR(50)", nullable: false),
                    ItemName = table.Column<string>(type: "VARCHAR(200)", nullable: true),
                    UnitOfMeasureCode = table.Column<string>(type: "VARCHAR(4)", nullable: true),
                    Direction = table.Column<int>(type: "int", nullable: false),
                    Quantity = table.Column<decimal>(type: "DECIMAL(19,6)", nullable: false),
                    TotalValue = table.Column<decimal>(type: "DECIMAL(19,2)", nullable: false),
                    Reason = table.Column<string>(type: "VARCHAR(500)", nullable: false),
                    RowId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<string>(type: "VARCHAR(100)", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<string>(type: "VARCHAR(100)", nullable: true),
                    ApprovedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ApprovedBy = table.Column<string>(type: "VARCHAR(100)", nullable: true),
                    CanceledAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CanceledBy = table.Column<string>(type: "VARCHAR(100)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FISCAL_STOCK_ADJUSTMENTS", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "FISCAL_STOCK_PERIODS",
                columns: table => new
                {
                    Key = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BranchCode = table.Column<string>(type: "VARCHAR(10)", nullable: false),
                    Year = table.Column<int>(type: "int", nullable: false),
                    Month = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    ClosedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ClosedBy = table.Column<string>(type: "VARCHAR(100)", nullable: true),
                    ReopenedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReopenedBy = table.Column<string>(type: "VARCHAR(100)", nullable: true),
                    ReopenReason = table.Column<string>(type: "VARCHAR(500)", nullable: true),
                    RowId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<string>(type: "VARCHAR(100)", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<string>(type: "VARCHAR(100)", nullable: true),
                    ApprovedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ApprovedBy = table.Column<string>(type: "VARCHAR(100)", nullable: true),
                    CanceledAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CanceledBy = table.Column<string>(type: "VARCHAR(100)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FISCAL_STOCK_PERIODS", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "FISCAL_STOCK_PERIOD_EVENTS",
                columns: table => new
                {
                    Key = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PeriodKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    EventType = table.Column<int>(type: "int", nullable: false),
                    At = table.Column<DateTime>(type: "datetime2", nullable: false),
                    By = table.Column<string>(type: "VARCHAR(100)", nullable: false),
                    Reason = table.Column<string>(type: "VARCHAR(500)", nullable: true),
                    RowId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<string>(type: "VARCHAR(100)", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<string>(type: "VARCHAR(100)", nullable: true),
                    ApprovedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ApprovedBy = table.Column<string>(type: "VARCHAR(100)", nullable: true),
                    CanceledAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CanceledBy = table.Column<string>(type: "VARCHAR(100)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FISCAL_STOCK_PERIOD_EVENTS", x => x.Key);
                    table.ForeignKey(
                        name: "FK_FISCAL_STOCK_PERIOD_EVENTS_FISCAL_STOCK_PERIODS_PeriodKey",
                        column: x => x.PeriodKey,
                        principalTable: "FISCAL_STOCK_PERIODS",
                        principalColumn: "Key",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FISCAL_STOCK_PERIOD_LAYERS",
                columns: table => new
                {
                    Key = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PeriodKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ItemCode = table.Column<string>(type: "VARCHAR(50)", nullable: false),
                    ItemName = table.Column<string>(type: "VARCHAR(200)", nullable: true),
                    UnitOfMeasureCode = table.Column<string>(type: "VARCHAR(4)", nullable: true),
                    LayerDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SourceLineKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Quantity = table.Column<decimal>(type: "DECIMAL(19,6)", nullable: false),
                    Value = table.Column<decimal>(type: "DECIMAL(19,2)", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    RowId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<string>(type: "VARCHAR(100)", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<string>(type: "VARCHAR(100)", nullable: true),
                    ApprovedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ApprovedBy = table.Column<string>(type: "VARCHAR(100)", nullable: true),
                    CanceledAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CanceledBy = table.Column<string>(type: "VARCHAR(100)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FISCAL_STOCK_PERIOD_LAYERS", x => x.Key);
                    table.ForeignKey(
                        name: "FK_FISCAL_STOCK_PERIOD_LAYERS_FISCAL_STOCK_PERIODS_PeriodKey",
                        column: x => x.PeriodKey,
                        principalTable: "FISCAL_STOCK_PERIODS",
                        principalColumn: "Key",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FISCAL_STOCK_ADJUSTMENTS_BranchCode_DocNumber",
                table: "FISCAL_STOCK_ADJUSTMENTS",
                columns: new[] { "BranchCode", "DocNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FISCAL_STOCK_PERIOD_EVENTS_PeriodKey",
                table: "FISCAL_STOCK_PERIOD_EVENTS",
                column: "PeriodKey");

            migrationBuilder.CreateIndex(
                name: "IX_FISCAL_STOCK_PERIOD_LAYERS_PeriodKey",
                table: "FISCAL_STOCK_PERIOD_LAYERS",
                column: "PeriodKey");

            migrationBuilder.CreateIndex(
                name: "IX_FISCAL_STOCK_PERIODS_BranchCode_Year_Month",
                table: "FISCAL_STOCK_PERIODS",
                columns: new[] { "BranchCode", "Year", "Month" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FISCAL_STOCK_ADJUSTMENTS");

            migrationBuilder.DropTable(
                name: "FISCAL_STOCK_PERIOD_EVENTS");

            migrationBuilder.DropTable(
                name: "FISCAL_STOCK_PERIOD_LAYERS");

            migrationBuilder.DropTable(
                name: "FISCAL_STOCK_PERIODS");
        }
    }
}
