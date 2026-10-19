using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.CommonContext
{
    /// <summary>
    /// Cria o grupo raiz "Fiscal" (Order 8, logo após "Financeiro") e move para ele as telas fiscais:
    /// Naturezas de Operação, Configuração de Retenções, Configuração da NF-e e ICMS por UF. Cada item mantém
    /// o seu StandaloneOnly. Também desabilita o item legado "Tributos/Impostos" (taxes) e põe "Relatórios" por último (Order 999).
    /// </summary>
    public partial class AddFiscalMenu : Migration
    {
        private const string FiscalRoleMenuId = "5D8E2A47-C163-4F9B-8E21-7A4C9B3D6F05";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "MENU_ITEMS",
                columns: ["Key", "Title", "Icon", "Enabled", "Expanded", "Order", "ParentKey", "StandaloneOnly"],
                values: new object[] { "fiscal", "Fiscal", "sap-icon://receipt", true, false, 8, null, false });

            migrationBuilder.InsertData(
                table: "ROLE_MENUS",
                columns: ["Id", "RoleCode", "MenuItemKey"],
                values: new object[] { FiscalRoleMenuId, "ADMIN", "fiscal" });

            MoveItem(migrationBuilder, "usages", "fiscal", 1);
            MoveItem(migrationBuilder, "withholdingTaxSettings", "fiscal", 2);
            MoveItem(migrationBuilder, "nfeSettings", "fiscal", 3);
            MoveItem(migrationBuilder, "icmsStateSettings", "fiscal", 4);

            migrationBuilder.UpdateData(table: "MENU_ITEMS", keyColumn: "Key", keyValue: "taxes",
                column: "Enabled", value: false);

            // "Relatórios" sempre por último entre os grupos raiz.
            migrationBuilder.UpdateData(table: "MENU_ITEMS", keyColumn: "Key", keyValue: "reports",
                column: "Order", value: 999);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(table: "MENU_ITEMS", keyColumn: "Key", keyValue: "taxes",
                column: "Enabled", value: true);

            migrationBuilder.UpdateData(table: "MENU_ITEMS", keyColumn: "Key", keyValue: "reports",
                column: "Order", value: 6);

            MoveItem(migrationBuilder, "usages", "registers", 13);
            MoveItem(migrationBuilder, "withholdingTaxSettings", "financial", 5);
            MoveItem(migrationBuilder, "nfeSettings", "registers", 17);
            MoveItem(migrationBuilder, "icmsStateSettings", "registers", 18);

            // ROLE_MENUS primeiro: tem FK para MENU_ITEMS.
            migrationBuilder.DeleteData(table: "ROLE_MENUS", keyColumn: "Id", keyValue: FiscalRoleMenuId);
            migrationBuilder.DeleteData(table: "MENU_ITEMS", keyColumn: "Key", keyValue: "fiscal");
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
