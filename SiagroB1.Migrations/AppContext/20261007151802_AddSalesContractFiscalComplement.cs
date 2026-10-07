using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class AddSalesContractFiscalComplement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CustomerOrderItem",
                table: "SALES_INVOICES_ITEMS",
                type: "VARCHAR(6)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CustomerOrderNumber",
                table: "SALES_INVOICES_ITEMS",
                type: "VARCHAR(15)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SALES_CONTRACT_FISCAL_COMPLEMENTS",
                columns: table => new
                {
                    SalesContractKey = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UsageCode = table.Column<int>(type: "int", nullable: true),
                    PaymentConditionCode = table.Column<int>(type: "int", nullable: true),
                    AdditionalInfo = table.Column<string>(type: "VARCHAR(2000)", nullable: true),
                    CustomerOrderNumber = table.Column<string>(type: "VARCHAR(15)", nullable: true),
                    CustomerOrderItem = table.Column<string>(type: "VARCHAR(6)", nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    UpdatedBy = table.Column<string>(type: "VARCHAR(100)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SALES_CONTRACT_FISCAL_COMPLEMENTS", x => x.SalesContractKey);
                    table.ForeignKey(
                        name: "FK_SALES_CONTRACT_FISCAL_COMPLEMENTS_SALES_CONTRACTS_SalesContractKey",
                        column: x => x.SalesContractKey,
                        principalTable: "SALES_CONTRACTS",
                        principalColumn: "Key",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SALES_CONTRACT_FISCAL_COMPLEMENTS");

            migrationBuilder.DropColumn(
                name: "CustomerOrderItem",
                table: "SALES_INVOICES_ITEMS");

            migrationBuilder.DropColumn(
                name: "CustomerOrderNumber",
                table: "SALES_INVOICES_ITEMS");
        }
    }
}
