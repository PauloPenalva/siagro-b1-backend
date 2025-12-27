#nullable disable

using Microsoft.EntityFrameworkCore.Migrations;

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class AlterTablePurchaseContractsAddColumnAgentCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AgentCode",
                table: "PURCHASE_CONTRACTS",
                type: "VARCHAR(10)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PURCHASE_CONTRACTS_AgentCode",
                table: "PURCHASE_CONTRACTS",
                column: "AgentCode");

            migrationBuilder.AddForeignKey(
                name: "FK_PURCHASE_CONTRACTS_AGENTS_AgentCode",
                table: "PURCHASE_CONTRACTS",
                column: "AgentCode",
                principalTable: "AGENTS",
                principalColumn: "Code");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PURCHASE_CONTRACTS_AGENTS_AgentCode",
                table: "PURCHASE_CONTRACTS");

            migrationBuilder.DropIndex(
                name: "IX_PURCHASE_CONTRACTS_AgentCode",
                table: "PURCHASE_CONTRACTS");

            migrationBuilder.DropColumn(
                name: "AgentCode",
                table: "PURCHASE_CONTRACTS");
        }
    }
}
