using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.CommonContext
{
    /// <summary>
    /// Menu "Transferências entre Contas" sob "Financeiro" (Order 9; 1–8 já ocupados). Vale nos dois modos
    /// (StandaloneOnly = false), como os Lançamentos da Conta: o financeiro vive no banco do Siagro também em SAPB1.
    ///
    /// A Key PRECISA ser igual ao name da rota no manifest.json do frontend: App.controller.ts navega com
    /// navTo(item.getKey()). Sem ROLE_MENUS o item não aparece.
    /// </summary>
    public partial class AddFinancialAccountTransfersMenu : Migration
    {
        private const string RoleMenuId = "3E7B9C14-5A26-4F83-9D41-C2B8E6A0F517";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "MENU_ITEMS",
                columns: ["Key", "Title", "Icon", "Enabled", "Expanded", "Order", "ParentKey", "StandaloneOnly"],
                values: new object[] { "financialAccountTransfers", "Transferências entre Contas", "sap-icon://folder-blank", true, false, 9, "financial", false });

            migrationBuilder.InsertData(
                table: "ROLE_MENUS",
                columns: ["Id", "RoleCode", "MenuItemKey"],
                values: new object[] { RoleMenuId, "ADMIN", "financialAccountTransfers" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // ROLE_MENUS primeiro: tem FK para MENU_ITEMS.
            migrationBuilder.DeleteData(table: "ROLE_MENUS", keyColumn: "Id", keyValue: RoleMenuId);
            migrationBuilder.DeleteData(table: "MENU_ITEMS", keyColumn: "Key", keyValue: "financialAccountTransfers");
        }
    }
}
