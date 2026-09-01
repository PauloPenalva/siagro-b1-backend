using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.CommonContext
{
    /// <summary>
    /// Menu do Painel de Cargas — o quadro diário das cargas planejadas, pedido para dar à
    /// diretoria a visão que hoje vive em planilhas paralelas e no WhatsApp.
    ///
    /// A Key PRECISA ser igual ao nome da rota no manifest.json: App.controller.ts navega com
    /// navTo(item.getKey()). Sem o vínculo em ROLE_MENUS o item não aparece para ninguém.
    ///
    /// Order 13 porque 1 a 12 já estão ocupados em "sales" (12 é a Montagem de Carga). Fica no
    /// fim do grupo para não renumerar itens existentes.
    /// </summary>
    public partial class AddShipmentLoadPanelMenu : Migration
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
                    {
                        "shipmentLoadsPanel", "Painel de Cargas",
                        "sap-icon://kanban-board", true, false, 13, "sales"
                    },
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
                    {
                        "F4A1C8B2-9D63-4E07-B5A8-2C41E9D70F86", "ADMIN",
                        "shipmentLoadsPanel"
                    },
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // ROLE_MENUS primeiro: tem FK para MENU_ITEMS.
            migrationBuilder.DeleteData(
                table: "ROLE_MENUS",
                keyColumn: "Id",
                keyValues: ["F4A1C8B2-9D63-4E07-B5A8-2C41E9D70F86"]);

            migrationBuilder.DeleteData(
                table: "MENU_ITEMS",
                keyColumn: "Key",
                keyValues: ["shipmentLoadsPanel"]);
        }
    }
}
