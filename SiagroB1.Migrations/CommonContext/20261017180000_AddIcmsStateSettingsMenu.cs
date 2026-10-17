using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.CommonContext
{
    /// <summary>
    /// Menu da tela "ICMS por UF" (alíquota interna, FCP e regra da base do DIFAL de cada UF), sob o grupo
    /// "Cadastros" (spec 2026-10-17, Efeito dos tributos e DIFAL). Só STANDALONE.
    ///
    /// A Key PRECISA ser igual ao name da rota no manifest.json do frontend: App.controller.ts navega com
    /// navTo(item.getKey()). Sem ROLE_MENUS o item não aparece.
    /// </summary>
    public partial class AddIcmsStateSettingsMenu : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "MENU_ITEMS",
                columns: ["Key", "Title", "Icon", "Enabled", "Expanded", "Order", "ParentKey", "StandaloneOnly"],
                values: new object[,]
                {
                    { "icmsStateSettings", "ICMS por UF", "sap-icon://action-settings", true, false, 18, "registers", true },
                });

            migrationBuilder.InsertData(
                table: "ROLE_MENUS",
                columns: ["Id", "RoleCode", "MenuItemKey"],
                values: new object[,]
                {
                    { "7B2D9E41-3C58-4A6F-9D10-E8A5C4B7F263", "ADMIN", "icmsStateSettings" },
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // ROLE_MENUS primeiro: tem FK para MENU_ITEMS.
            migrationBuilder.DeleteData(table: "ROLE_MENUS", keyColumn: "Id", keyValue: "7B2D9E41-3C58-4A6F-9D10-E8A5C4B7F263");
            migrationBuilder.DeleteData(table: "MENU_ITEMS", keyColumn: "Key", keyValue: "icmsStateSettings");
        }
    }
}
