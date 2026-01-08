using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class AlterTablePurchaseContractsAddColumnAgentName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PURCHASE_CONTRACTS_AGENTS_AgentCode",
                table: "PURCHASE_CONTRACTS");

            migrationBuilder.DropForeignKey(
                name: "FK_SALES_CONTRACTS_AGENTS_AgentCode",
                table: "SALES_CONTRACTS");

            migrationBuilder.DropIndex(
                name: "IX_SALES_CONTRACTS_AgentCode",
                table: "SALES_CONTRACTS");

            migrationBuilder.DropIndex(
                name: "IX_PURCHASE_CONTRACTS_AgentCode",
                table: "PURCHASE_CONTRACTS");

            migrationBuilder.AlterColumn<int>(
                name: "AgentCode",
                table: "SALES_CONTRACTS",
                type: "int",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "VARCHAR(10)",
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AgentName",
                table: "SALES_CONTRACTS",
                type: "VARCHAR(100)",
                nullable: true);

            migrationBuilder.AlterColumn<int>(
                name: "AgentCode",
                table: "PURCHASE_CONTRACTS",
                type: "int",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "VARCHAR(10)",
                oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AgentName",
                table: "PURCHASE_CONTRACTS",
                type: "VARCHAR(100)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AgentName",
                table: "SALES_CONTRACTS");

            migrationBuilder.DropColumn(
                name: "AgentName",
                table: "PURCHASE_CONTRACTS");

            migrationBuilder.AlterColumn<string>(
                name: "AgentCode",
                table: "SALES_CONTRACTS",
                type: "VARCHAR(10)",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "AgentCode",
                table: "PURCHASE_CONTRACTS",
                type: "VARCHAR(10)",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_SALES_CONTRACTS_AgentCode",
                table: "SALES_CONTRACTS",
                column: "AgentCode");

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

            migrationBuilder.AddForeignKey(
                name: "FK_SALES_CONTRACTS_AGENTS_AgentCode",
                table: "SALES_CONTRACTS",
                column: "AgentCode",
                principalTable: "AGENTS",
                principalColumn: "Code");
        }
    }
}
