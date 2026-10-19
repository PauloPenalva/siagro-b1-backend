using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.CommonContext
{
    /// <summary>
    /// Menus de Lançamentos da Conta, Reconciliação de Conta e Extrato da Conta, sob "Financeiro" (Order 6–8; 1–5 já
    /// ocupados). Valem nos dois modos (StandaloneOnly = false): o financeiro vive no banco do Siagro também em SAPB1.
    ///
    /// A Key PRECISA ser igual ao name da rota no manifest.json do frontend: App.controller.ts navega com
    /// navTo(item.getKey()). Sem ROLE_MENUS o item não aparece.
    /// </summary>
    public partial class AddFinancialAccountMovementsMenus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "MENU_ITEMS",
                columns: ["Key", "Title", "Icon", "Enabled", "Expanded", "Order", "ParentKey", "StandaloneOnly"],
                values: new object[,]
                {
                    { "financialAccountMovements", "Lançamentos da Conta", "sap-icon://folder-blank", true, false, 6, "financial", false },
                    { "financialAccountReconciliations", "Reconciliação de Conta", "sap-icon://folder-blank", true, false, 7, "financial", false },
                    { "financialAccountStatementReport", "Extrato da Conta", "sap-icon://print", true, false, 8, "financial", false },
                });

            migrationBuilder.InsertData(
                table: "ROLE_MENUS",
                columns: ["Id", "RoleCode", "MenuItemKey"],
                values: new object[,]
                {
                    { "7A2C4E61-9D83-4B15-A6E2-1F8D3C5B7A90", "ADMIN", "financialAccountMovements" },
                    { "8B3D5F72-AE94-4C26-B7F3-209E4D6C8BA1", "ADMIN", "financialAccountReconciliations" },
                    { "9C4E6083-BFA5-4D37-88A4-31AF5E7D9CB2", "ADMIN", "financialAccountStatementReport" },
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // ROLE_MENUS primeiro: tem FK para MENU_ITEMS.
            migrationBuilder.DeleteData(table: "ROLE_MENUS", keyColumn: "Id", keyValues:
            [
                "7A2C4E61-9D83-4B15-A6E2-1F8D3C5B7A90", "8B3D5F72-AE94-4C26-B7F3-209E4D6C8BA1",
                "9C4E6083-BFA5-4D37-88A4-31AF5E7D9CB2"
            ]);
            migrationBuilder.DeleteData(table: "MENU_ITEMS", keyColumn: "Key", keyValues:
                ["financialAccountMovements", "financialAccountReconciliations", "financialAccountStatementReport"]);
        }
    }
}
