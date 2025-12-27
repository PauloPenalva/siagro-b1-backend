#nullable disable

using Microsoft.EntityFrameworkCore.Migrations;

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class AlterTableSalesContractAddColumnCardNameAndItemName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AgentCode",
                table: "SALES_CONTRACTS",
                type: "VARCHAR(10)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CardName",
                table: "SALES_CONTRACTS",
                type: "VARCHAR(200)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ItemName",
                table: "SALES_CONTRACTS",
                type: "VARCHAR(200)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_SALES_CONTRACTS_AgentCode",
                table: "SALES_CONTRACTS",
                column: "AgentCode");

            migrationBuilder.AddForeignKey(
                name: "FK_SALES_CONTRACTS_AGENTS_AgentCode",
                table: "SALES_CONTRACTS",
                column: "AgentCode",
                principalTable: "AGENTS",
                principalColumn: "Code");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SALES_CONTRACTS_AGENTS_AgentCode",
                table: "SALES_CONTRACTS");

            migrationBuilder.DropIndex(
                name: "IX_SALES_CONTRACTS_AgentCode",
                table: "SALES_CONTRACTS");

            migrationBuilder.DropColumn(
                name: "AgentCode",
                table: "SALES_CONTRACTS");

            migrationBuilder.DropColumn(
                name: "CardName",
                table: "SALES_CONTRACTS");

            migrationBuilder.DropColumn(
                name: "ItemName",
                table: "SALES_CONTRACTS");
        }
    }
}
