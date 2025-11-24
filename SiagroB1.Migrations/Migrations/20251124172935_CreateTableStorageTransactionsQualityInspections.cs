using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class CreateTableStorageTransactionsQualityInspections : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ProcessingCostCode",
                table: "STORAGE_TRANSACTIONS",
                type: "VARCHAR(10)",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ProcessingCostKey",
                table: "STORAGE_TRANSACTIONS",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "STORAGE_TRANSACTIONS_QUALITY_INSPECTIONS",
                columns: table => new
                {
                    Key = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StorageTransactionKey = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    QualityAttribCode = table.Column<string>(type: "VARCHAR(10)", nullable: false),
                    Value = table.Column<decimal>(type: "DECIMAL(18,1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_STORAGE_TRANSACTIONS_QUALITY_INSPECTIONS", x => x.Key);
                    table.ForeignKey(
                        name: "FK_STORAGE_TRANSACTIONS_QUALITY_INSPECTIONS_QUALITY_ATTRIBS_QualityAttribCode",
                        column: x => x.QualityAttribCode,
                        principalTable: "QUALITY_ATTRIBS",
                        principalColumn: "Code",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_STORAGE_TRANSACTIONS_QUALITY_INSPECTIONS_STORAGE_TRANSACTIONS_StorageTransactionKey",
                        column: x => x.StorageTransactionKey,
                        principalTable: "STORAGE_TRANSACTIONS",
                        principalColumn: "Key");
                });

            migrationBuilder.CreateIndex(
                name: "IX_STORAGE_TRANSACTIONS_ProcessingCostCode",
                table: "STORAGE_TRANSACTIONS",
                column: "ProcessingCostCode");

            migrationBuilder.CreateIndex(
                name: "IX_STORAGE_TRANSACTIONS_QUALITY_INSPECTIONS_QualityAttribCode",
                table: "STORAGE_TRANSACTIONS_QUALITY_INSPECTIONS",
                column: "QualityAttribCode");

            migrationBuilder.CreateIndex(
                name: "IX_STORAGE_TRANSACTIONS_QUALITY_INSPECTIONS_StorageTransactionKey",
                table: "STORAGE_TRANSACTIONS_QUALITY_INSPECTIONS",
                column: "StorageTransactionKey");

            migrationBuilder.AddForeignKey(
                name: "FK_STORAGE_TRANSACTIONS_PROCESSING_COSTS_ProcessingCostCode",
                table: "STORAGE_TRANSACTIONS",
                column: "ProcessingCostCode",
                principalTable: "PROCESSING_COSTS",
                principalColumn: "Code");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_STORAGE_TRANSACTIONS_PROCESSING_COSTS_ProcessingCostCode",
                table: "STORAGE_TRANSACTIONS");

            migrationBuilder.DropTable(
                name: "STORAGE_TRANSACTIONS_QUALITY_INSPECTIONS");

            migrationBuilder.DropIndex(
                name: "IX_STORAGE_TRANSACTIONS_ProcessingCostCode",
                table: "STORAGE_TRANSACTIONS");

            migrationBuilder.DropColumn(
                name: "ProcessingCostCode",
                table: "STORAGE_TRANSACTIONS");

            migrationBuilder.DropColumn(
                name: "ProcessingCostKey",
                table: "STORAGE_TRANSACTIONS");
        }
    }
}
