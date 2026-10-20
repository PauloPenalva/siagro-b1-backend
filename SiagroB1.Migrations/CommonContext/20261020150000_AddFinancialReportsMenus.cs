using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.CommonContext
{
    /// <summary>
    /// Menus dos relatórios financeiros, sob "Financeiro" (Order 10–14). Valem nos dois modos
    /// (StandaloneOnly = false).
    ///
    /// A Key PRECISA ser igual ao name da rota no manifest.json do frontend: App.controller.ts navega com
    /// navTo(item.getKey()). Sem ROLE_MENUS o item não aparece.
    /// </summary>
    public partial class AddFinancialReportsMenus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "MENU_ITEMS",
                columns: ["Key", "Title", "Icon", "Enabled", "Expanded", "Order", "ParentKey", "StandaloneOnly"],
                values: new object[,]
                {
                    { "receivablesReport", "Relatório de Contas a Receber", "sap-icon://print", true, false, 10, "financial", false },
                    { "payablesReport", "Relatório de Contas a Pagar", "sap-icon://print", true, false, 11, "financial", false },
                    { "receivableSettlementsReport", "Baixas de Recebíveis", "sap-icon://print", true, false, 12, "financial", false },
                    { "payableSettlementsReport", "Baixas de Pagáveis", "sap-icon://print", true, false, 13, "financial", false },
                    { "partnerStatementReport", "Extrato do Parceiro", "sap-icon://print", true, false, 14, "financial", false },
                });

            migrationBuilder.InsertData(
                table: "ROLE_MENUS",
                columns: ["Id", "RoleCode", "MenuItemKey"],
                values: new object[,]
                {
                    { "A1D5F7B2-3C48-4E69-9A0B-5D7E1F2C8A31", "ADMIN", "receivablesReport" },
                    { "B2E6A8C3-4D59-4F7A-8B1C-6E8F2A3D9B42", "ADMIN", "payablesReport" },
                    { "C3F7B9D4-5E6A-4A8B-9C2D-7F9A3B4EAC53", "ADMIN", "receivableSettlementsReport" },
                    { "D4A8CAE5-6F7B-4B9C-8D3E-8AAB4C5FBD64", "ADMIN", "payableSettlementsReport" },
                    { "E5B9DBF6-7A8C-4CAD-9E4F-9BBC5D6ACE75", "ADMIN", "partnerStatementReport" },
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // ROLE_MENUS primeiro: tem FK para MENU_ITEMS.
            migrationBuilder.DeleteData(table: "ROLE_MENUS", keyColumn: "Id", keyValues:
            [
                "A1D5F7B2-3C48-4E69-9A0B-5D7E1F2C8A31", "B2E6A8C3-4D59-4F7A-8B1C-6E8F2A3D9B42",
                "C3F7B9D4-5E6A-4A8B-9C2D-7F9A3B4EAC53", "D4A8CAE5-6F7B-4B9C-8D3E-8AAB4C5FBD64",
                "E5B9DBF6-7A8C-4CAD-9E4F-9BBC5D6ACE75"
            ]);
            migrationBuilder.DeleteData(table: "MENU_ITEMS", keyColumn: "Key", keyValues:
            [
                "receivablesReport", "payablesReport", "receivableSettlementsReport", "payableSettlementsReport",
                "partnerStatementReport"
            ]);
        }
    }
}
