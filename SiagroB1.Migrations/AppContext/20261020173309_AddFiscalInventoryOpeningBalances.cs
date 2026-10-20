using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class AddFiscalInventoryOpeningBalances : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FISCAL_INVENTORY_OPENING_BALANCES",
                columns: table => new
                {
                    Key = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BranchCode = table.Column<string>(type: "VARCHAR(10)", nullable: false),
                    ItemCode = table.Column<string>(type: "VARCHAR(50)", nullable: false),
                    ItemName = table.Column<string>(type: "VARCHAR(200)", nullable: true),
                    BalanceDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Quantity = table.Column<decimal>(type: "DECIMAL(19,6)", nullable: false),
                    TotalValue = table.Column<decimal>(type: "DECIMAL(19,2)", nullable: false),
                    UnitOfMeasureCode = table.Column<string>(type: "VARCHAR(4)", nullable: true),
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
                    table.PrimaryKey("PK_FISCAL_INVENTORY_OPENING_BALANCES", x => x.Key);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FISCAL_INVENTORY_OPENING_BALANCES_BranchCode_ItemCode",
                table: "FISCAL_INVENTORY_OPENING_BALANCES",
                columns: new[] { "BranchCode", "ItemCode" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FISCAL_INVENTORY_OPENING_BALANCES");
        }
    }
}
