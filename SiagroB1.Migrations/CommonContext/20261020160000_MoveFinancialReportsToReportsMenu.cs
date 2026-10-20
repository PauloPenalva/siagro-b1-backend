using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.CommonContext
{
    /// <summary>
    /// Move os relatórios financeiros (Extrato da Conta, Contas a Receber/Pagar, Baixas de Recebíveis/Pagáveis e Extrato do
    /// Parceiro) do grupo "Financeiro" para "Relatórios", depois do último item existente (Order 21). Só dados; ROLE_MENUS
    /// não muda (as permissões seguem a chave do item, não o grupo).
    /// </summary>
    public partial class MoveFinancialReportsToReportsMenu : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            MoveItem(migrationBuilder, "financialAccountStatementReport", "reports", 22);
            MoveItem(migrationBuilder, "receivablesReport", "reports", 23);
            MoveItem(migrationBuilder, "payablesReport", "reports", 24);
            MoveItem(migrationBuilder, "receivableSettlementsReport", "reports", 25);
            MoveItem(migrationBuilder, "payableSettlementsReport", "reports", 26);
            MoveItem(migrationBuilder, "partnerStatementReport", "reports", 27);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            MoveItem(migrationBuilder, "financialAccountStatementReport", "financial", 8);
            MoveItem(migrationBuilder, "receivablesReport", "financial", 10);
            MoveItem(migrationBuilder, "payablesReport", "financial", 11);
            MoveItem(migrationBuilder, "receivableSettlementsReport", "financial", 12);
            MoveItem(migrationBuilder, "payableSettlementsReport", "financial", 13);
            MoveItem(migrationBuilder, "partnerStatementReport", "financial", 14);
        }

        private static void MoveItem(MigrationBuilder migrationBuilder, string key, string parentKey, int order) =>
            migrationBuilder.UpdateData(
                table: "MENU_ITEMS",
                keyColumn: "Key",
                keyValue: key,
                columns: ["ParentKey", "Order"],
                values: new object[] { parentKey, order });
    }
}
