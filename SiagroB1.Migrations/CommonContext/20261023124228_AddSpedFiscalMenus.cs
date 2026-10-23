using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.CommonContext
{
    /// <inheritdoc />
    public partial class AddSpedFiscalMenus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // A Key PRECISA ser igual ao name da rota no manifest.json do frontend
            // (App.controller.ts navega com navTo(item.getKey())). Sem ROLE_MENUS o item não aparece.
            // StandaloneOnly: o SPED Fiscal só existe em STANDALONE. Ordens 9 e 10: o AddStockMenu tirou do
            // grupo "fiscal" os itens de estoque que ocupavam essas posições.
            migrationBuilder.InsertData(
                table: "MENU_ITEMS",
                columns: ["Key", "Title", "Icon", "Enabled", "Expanded", "Order", "ParentKey", "StandaloneOnly"],
                values: new object[,]
                {
                    { "spedFiscal", "SPED Fiscal", "sap-icon://document-text", true, false, 9, "fiscal", true },
                    { "spedFiscalSettings", "Parâmetros do SPED Fiscal", "sap-icon://document-text", true, false, 10, "fiscal", true },
                });

            migrationBuilder.InsertData(
                table: "ROLE_MENUS",
                columns: ["Id", "RoleCode", "MenuItemKey"],
                values: new object[,]
                {
                    { "7D2E4A81-5C93-4F06-B8A7-1E9C3D5F6A20", "ADMIN", "spedFiscal" },
                    { "B4F81C6D-2A57-4E93-9D10-8C6E7A3B5F42", "ADMIN", "spedFiscalSettings" },
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // ROLE_MENUS primeiro: tem FK para MENU_ITEMS.
            migrationBuilder.DeleteData(table: "ROLE_MENUS", keyColumn: "Id", keyValue: "7D2E4A81-5C93-4F06-B8A7-1E9C3D5F6A20");
            migrationBuilder.DeleteData(table: "ROLE_MENUS", keyColumn: "Id", keyValue: "B4F81C6D-2A57-4E93-9D10-8C6E7A3B5F42");
            migrationBuilder.DeleteData(table: "MENU_ITEMS", keyColumn: "Key", keyValue: "spedFiscal");
            migrationBuilder.DeleteData(table: "MENU_ITEMS", keyColumn: "Key", keyValue: "spedFiscalSettings");
        }
    }
}
