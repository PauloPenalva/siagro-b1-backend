using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.CommonContext
{
    /// <summary>
    /// Menus do módulo financeiro. O grupo "Financeiro" é RAIZ (ParentKey = null), Order 7 — os
    /// raízes ocupados são main 0, admin 1, registers 2, storage 3, purchases 4, sales 5 e
    /// reports 6; entra no fim para não renumerar nada.
    ///
    /// A Key de cada item PRECISA ser igual ao name da rota no manifest.json do frontend:
    /// App.controller.ts navega com navTo(item.getKey()).
    ///
    /// Sem a linha em ROLE_MENUS o item não aparece para ninguém — inclusive o próprio grupo.
    /// </summary>
    public partial class AddFinancialMenus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "MENU_ITEMS",
                columns:
                [
                    "Key",
                    "Title",
                    "Icon",
                    "Enabled",
                    "Expanded",
                    "Order",
                    "ParentKey"
                ],
                values: new object[,]
                {
                    { "financial", "Financeiro", "sap-icon://money-bills", true, false, 7, null },
                    { "financialAccounts", "Contas Financeiras", "sap-icon://folder-blank", true, false, 1, "financial" },
                    { "accountsPayable", "Contas a Pagar", "sap-icon://folder-blank", true, false, 2, "financial" },
                    { "accountsReceivable", "Contas a Receber", "sap-icon://folder-blank", true, false, 3, "financial" },
                    { "financialAdvances", "Adiantamentos", "sap-icon://folder-blank", true, false, 4, "financial" }
                });

            migrationBuilder.InsertData(
                table: "ROLE_MENUS",
                columns:
                [
                    "Id",
                    "RoleCode",
                    "MenuItemKey"
                ],
                values: new object[,]
                {
                    { "A1D4E7F0-2B36-4C58-9E71-0D3A6B8C5F21", "ADMIN", "financial" },
                    { "B2E5F801-3C47-4D69-8F82-1E4B7C9D6032", "ADMIN", "financialAccounts" },
                    { "C3F60912-4D58-4E7A-9083-2F5C8D0E7143", "ADMIN", "accountsPayable" },
                    { "D4071A23-5E69-4F8B-A194-306D9E1F8254", "ADMIN", "accountsReceivable" },
                    { "E5182B34-6F7A-409C-B2A5-417EAF209365", "ADMIN", "financialAdvances" }
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // ROLE_MENUS primeiro: tem FK para MENU_ITEMS.
            migrationBuilder.DeleteData(
                table: "ROLE_MENUS",
                keyColumn: "Id",
                keyValues:
                [
                    "A1D4E7F0-2B36-4C58-9E71-0D3A6B8C5F21", "B2E5F801-3C47-4D69-8F82-1E4B7C9D6032",
                    "C3F60912-4D58-4E7A-9083-2F5C8D0E7143", "D4071A23-5E69-4F8B-A194-306D9E1F8254",
                    "E5182B34-6F7A-409C-B2A5-417EAF209365"
                ]);

            // Filhos antes do pai, pela auto-relação ParentKey.
            migrationBuilder.DeleteData(
                table: "MENU_ITEMS",
                keyColumn: "Key",
                keyValues: ["financialAccounts", "accountsPayable", "accountsReceivable", "financialAdvances"]);

            migrationBuilder.DeleteData(
                table: "MENU_ITEMS",
                keyColumn: "Key",
                keyValues: ["financial"]);
        }
    }
}
