using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.CommonContext
{
    /// <inheritdoc />
    public partial class AddFiscalStockAdjustmentAndClosingMenus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // A Key PRECISA ser igual ao name da rota no manifest.json do frontend
            // (App.controller.ts navega com navTo(item.getKey())). Sem ROLE_MENUS o item não aparece.
            // StandaloneOnly: ajustes e fechamento só existem em STANDALONE. Orders 9 e 10: logo após o
            // Kardex Fiscal (8); o Inventário em Data passa de 9 para 11.
            migrationBuilder.UpdateData(
                table: "MENU_ITEMS", keyColumn: "Key", keyValue: "fiscalInventory", column: "Order", value: 11);

            migrationBuilder.InsertData(
                table: "MENU_ITEMS",
                columns: ["Key", "Title", "Icon", "Enabled", "Expanded", "Order", "ParentKey", "StandaloneOnly"],
                values: new object[,]
                {
                    { "fiscalStockAdjustments", "Ajustes de Estoque Fiscal", "sap-icon://edit", true, false, 9, "fiscal", true },
                    { "fiscalStockPeriods", "Fechamento do Estoque Fiscal", "sap-icon://locked", true, false, 10, "fiscal", true },
                });

            migrationBuilder.InsertData(
                table: "ROLE_MENUS",
                columns: ["Id", "RoleCode", "MenuItemKey"],
                values: new object[,]
                {
                    { "7C3E9A15-D2B8-4F60-A1E4-3B95C0D87F26", "ADMIN", "fiscalStockAdjustments" },
                    { "E4A81D07-5B6C-49F3-8D2A-C1F7096E3B58", "ADMIN", "fiscalStockPeriods" },
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // ROLE_MENUS primeiro: tem FK para MENU_ITEMS.
            migrationBuilder.DeleteData(table: "ROLE_MENUS", keyColumn: "Id", keyValue: "7C3E9A15-D2B8-4F60-A1E4-3B95C0D87F26");
            migrationBuilder.DeleteData(table: "ROLE_MENUS", keyColumn: "Id", keyValue: "E4A81D07-5B6C-49F3-8D2A-C1F7096E3B58");
            migrationBuilder.DeleteData(table: "MENU_ITEMS", keyColumn: "Key", keyValue: "fiscalStockAdjustments");
            migrationBuilder.DeleteData(table: "MENU_ITEMS", keyColumn: "Key", keyValue: "fiscalStockPeriods");
            migrationBuilder.UpdateData(
                table: "MENU_ITEMS", keyColumn: "Key", keyValue: "fiscalInventory", column: "Order", value: 9);
        }
    }
}
