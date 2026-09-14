using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class AddWarehouseReconciliations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "WAREHOUSE_RECONCILIATION_REASONS",
                columns: table => new
                {
                    Key = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "VARCHAR(20)", nullable: false),
                    Description = table.Column<string>(type: "VARCHAR(100)", nullable: false),
                    Active = table.Column<bool>(type: "bit", nullable: false),
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
                    table.PrimaryKey("PK_WAREHOUSE_RECONCILIATION_REASONS", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "WAREHOUSE_RECONCILIATIONS",
                columns: table => new
                {
                    Key = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "VARCHAR(50)", nullable: true),
                    Status = table.Column<int>(type: "int", nullable: false),
                    WarehouseCode = table.Column<string>(type: "VARCHAR(50)", nullable: false),
                    WarehouseName = table.Column<string>(type: "VARCHAR(200)", nullable: true),
                    ItemCode = table.Column<string>(type: "VARCHAR(50)", nullable: false),
                    ItemName = table.Column<string>(type: "VARCHAR(200)", nullable: true),
                    UnitOfMeasureCode = table.Column<string>(type: "VARCHAR(10)", nullable: false),
                    CardCode = table.Column<string>(type: "VARCHAR(50)", nullable: true),
                    CardName = table.Column<string>(type: "VARCHAR(200)", nullable: true),
                    ReferenceDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ReportedBalance = table.Column<decimal>(type: "DECIMAL(18,3)", nullable: false),
                    SystemBalance = table.Column<decimal>(type: "DECIMAL(18,3)", nullable: false),
                    Difference = table.Column<decimal>(type: "DECIMAL(18,3)", nullable: false),
                    ReasonKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Comments = table.Column<string>(type: "VARCHAR(500)", nullable: true),
                    SentAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SentBy = table.Column<string>(type: "VARCHAR(100)", nullable: true),
                    ApprovalComments = table.Column<string>(type: "VARCHAR(500)", nullable: true),
                    CancellationReason = table.Column<string>(type: "VARCHAR(500)", nullable: true),
                    StorageTransactionKey = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true),
                    RowId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<string>(type: "VARCHAR(100)", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<string>(type: "VARCHAR(100)", nullable: true),
                    ApprovedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ApprovedBy = table.Column<string>(type: "VARCHAR(100)", nullable: true),
                    CanceledAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CanceledBy = table.Column<string>(type: "VARCHAR(100)", nullable: true),
                    DocNumberKey = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    BranchCode = table.Column<string>(type: "VARCHAR(14)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WAREHOUSE_RECONCILIATIONS", x => x.Key);
                    table.ForeignKey(
                        name: "FK_WAREHOUSE_RECONCILIATIONS_BRANCHS_BranchCode",
                        column: x => x.BranchCode,
                        principalTable: "BRANCHS",
                        principalColumn: "Code");
                    table.ForeignKey(
                        name: "FK_WAREHOUSE_RECONCILIATIONS_DOC_NUMBERS_DocNumberKey",
                        column: x => x.DocNumberKey,
                        principalTable: "DOC_NUMBERS",
                        principalColumn: "Key");
                    table.ForeignKey(
                        name: "FK_WAREHOUSE_RECONCILIATIONS_WAREHOUSE_RECONCILIATION_REASONS_ReasonKey",
                        column: x => x.ReasonKey,
                        principalTable: "WAREHOUSE_RECONCILIATION_REASONS",
                        principalColumn: "Key",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "WAREHOUSE_RECONCILIATION_ATTACHMENTS",
                columns: table => new
                {
                    Key = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    WarehouseReconciliationKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Description = table.Column<string>(type: "VARCHAR(100)", nullable: false),
                    FileName = table.Column<string>(type: "VARCHAR(100)", nullable: false),
                    FileData = table.Column<byte[]>(type: "VARBINARY(MAX)", nullable: false),
                    ContentType = table.Column<string>(type: "VARCHAR(100)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CreatedBy = table.Column<string>(type: "VARCHAR(100)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WAREHOUSE_RECONCILIATION_ATTACHMENTS", x => x.Key);
                    table.ForeignKey(
                        name: "FK_WAREHOUSE_RECONCILIATION_ATTACHMENTS_WAREHOUSE_RECONCILIATIONS_WarehouseReconciliationKey",
                        column: x => x.WarehouseReconciliationKey,
                        principalTable: "WAREHOUSE_RECONCILIATIONS",
                        principalColumn: "Key",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WAREHOUSE_RECONCILIATION_ATTACHMENTS_WarehouseReconciliationKey",
                table: "WAREHOUSE_RECONCILIATION_ATTACHMENTS",
                column: "WarehouseReconciliationKey");

            migrationBuilder.CreateIndex(
                name: "IX_WAREHOUSE_RECONCILIATION_REASONS_Code",
                table: "WAREHOUSE_RECONCILIATION_REASONS",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WAREHOUSE_RECONCILIATIONS_BranchCode",
                table: "WAREHOUSE_RECONCILIATIONS",
                column: "BranchCode");

            migrationBuilder.CreateIndex(
                name: "IX_WAREHOUSE_RECONCILIATIONS_DocNumberKey",
                table: "WAREHOUSE_RECONCILIATIONS",
                column: "DocNumberKey");

            migrationBuilder.CreateIndex(
                name: "IX_WAREHOUSE_RECONCILIATIONS_OpenPerWarehouseItem",
                table: "WAREHOUSE_RECONCILIATIONS",
                columns: new[] { "WarehouseCode", "ItemCode" },
                unique: true,
                filter: "[Status] IN (0, 1)");

            migrationBuilder.CreateIndex(
                name: "IX_WAREHOUSE_RECONCILIATIONS_ReasonKey",
                table: "WAREHOUSE_RECONCILIATIONS",
                column: "ReasonKey");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WAREHOUSE_RECONCILIATION_ATTACHMENTS");

            migrationBuilder.DropTable(
                name: "WAREHOUSE_RECONCILIATIONS");

            migrationBuilder.DropTable(
                name: "WAREHOUSE_RECONCILIATION_REASONS");
        }
    }
}
