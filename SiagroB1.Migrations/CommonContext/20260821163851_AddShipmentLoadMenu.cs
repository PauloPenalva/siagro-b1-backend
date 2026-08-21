using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.CommonContext
{
    /// <summary>
    /// Menu da Montagem de Carga, no grupo "Vendas" — a etapa nova entre a Expedição de Grãos
    /// e o Faturamento de Expedição.
    ///
    /// A Key PRECISA ser igual ao nome da rota no manifest.json: App.controller.ts navega com
    /// navTo(item.getKey()). Sem o vínculo em ROLE_MENUS o item não aparece para ninguém.
    ///
    /// Order 12 porque 1 a 11 já estão ocupados em "sales" (11 aparece duas vezes). Fica no fim
    /// do grupo, e não ao lado do Faturamento de Expedição (Order 4), para não renumerar itens
    /// existentes.
    /// </summary>
    public partial class AddShipmentLoadMenu : Migration
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
                        "shipmentLoads", "Montagem de Carga",
                        "sap-icon://shipping-status", true, false, 12, "sales"
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
                        "6D2E9A47-3C81-4B50-A9F6-1E70B85C2D93", "ADMIN",
                        "shipmentLoads"
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
                keyValues: ["6D2E9A47-3C81-4B50-A9F6-1E70B85C2D93"]);

            migrationBuilder.DeleteData(
                table: "MENU_ITEMS",
                keyColumn: "Key",
                keyValues: ["shipmentLoads"]);
        }
    }
}
