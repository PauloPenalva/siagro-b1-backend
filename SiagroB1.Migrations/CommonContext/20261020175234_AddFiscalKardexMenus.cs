using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.CommonContext
{
    /// <inheritdoc />
    public partial class AddFiscalKardexMenus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // A Key PRECISA ser igual ao name da rota no manifest.json do frontend
            // (App.controller.ts navega com navTo(item.getKey())). Sem ROLE_MENUS o item não aparece.
            // StandaloneOnly: Kardex e saldo inicial só existem em STANDALONE. Orders 6 e 7: logo após a
            // Conferência Fiscal (5), no grupo "fiscal".
            migrationBuilder.InsertData(
                table: "MENU_ITEMS",
                columns: ["Key", "Title", "Icon", "Enabled", "Expanded", "Order", "ParentKey", "StandaloneOnly"],
                values: new object[,]
                {
                    { "fiscalInventoryOpeningBalances", "Saldo Inicial do Estoque", "sap-icon://inventory", true, false, 6, "fiscal", true },
                    { "fiscalKardex", "Kardex Fiscal", "sap-icon://list", true, false, 7, "fiscal", true },
                });

            migrationBuilder.InsertData(
                table: "ROLE_MENUS",
                columns: ["Id", "RoleCode", "MenuItemKey"],
                values: new object[,]
                {
                    { "5B8E2F41-9C37-4A6D-8E15-D4A07C3B6F92", "ADMIN", "fiscalInventoryOpeningBalances" },
                    { "A2D64C98-1F5B-47E3-B90A-7E3C85D1F604", "ADMIN", "fiscalKardex" },
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // ROLE_MENUS primeiro: tem FK para MENU_ITEMS.
            migrationBuilder.DeleteData(table: "ROLE_MENUS", keyColumn: "Id", keyValue: "5B8E2F41-9C37-4A6D-8E15-D4A07C3B6F92");
            migrationBuilder.DeleteData(table: "ROLE_MENUS", keyColumn: "Id", keyValue: "A2D64C98-1F5B-47E3-B90A-7E3C85D1F604");
            migrationBuilder.DeleteData(table: "MENU_ITEMS", keyColumn: "Key", keyValue: "fiscalInventoryOpeningBalances");
            migrationBuilder.DeleteData(table: "MENU_ITEMS", keyColumn: "Key", keyValue: "fiscalKardex");
        }
    }
}
