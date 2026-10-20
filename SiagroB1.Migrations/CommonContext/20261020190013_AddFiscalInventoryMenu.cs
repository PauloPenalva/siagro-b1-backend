using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.CommonContext
{
    /// <inheritdoc />
    public partial class AddFiscalInventoryMenu : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // A Key PRECISA ser igual ao name da rota no manifest.json do frontend
            // (App.controller.ts navega com navTo(item.getKey())). Sem ROLE_MENUS o item não aparece.
            // StandaloneOnly: o Inventário em Data lê o Kardex Fiscal, que só existe em STANDALONE.
            // Order 8: logo após o Kardex Fiscal (7), no grupo "fiscal".
            migrationBuilder.InsertData(
                table: "MENU_ITEMS",
                columns: ["Key", "Title", "Icon", "Enabled", "Expanded", "Order", "ParentKey", "StandaloneOnly"],
                values: new object[] { "fiscalInventory", "Inventário em Data", "sap-icon://product", true, false, 8, "fiscal", true });

            migrationBuilder.InsertData(
                table: "ROLE_MENUS",
                columns: ["Id", "RoleCode", "MenuItemKey"],
                values: new object[] { "A991C3FC-AA80-4C4D-A830-AD75728379DD", "ADMIN", "fiscalInventory" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // ROLE_MENUS primeiro: tem FK para MENU_ITEMS.
            migrationBuilder.DeleteData(table: "ROLE_MENUS", keyColumn: "Id", keyValue: "A991C3FC-AA80-4C4D-A830-AD75728379DD");
            migrationBuilder.DeleteData(table: "MENU_ITEMS", keyColumn: "Key", keyValue: "fiscalInventory");
        }
    }
}
