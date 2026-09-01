using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.CommonContext
{
    /// <summary>
    /// Menu dos Romaneios de Embarque — a lista dos romaneios de embarque de venda com a carga a
    /// que cada um pertence, e o único lugar em que o estorno do embarque
    /// (ShippingTransactionsReverse) volta a ser alcançável depois que a vinculação de romaneios
    /// virou página própria.
    ///
    /// A rota storageTransactionsSales já existia no manifest.json, mas nunca teve item de menu:
    /// a tela só era alcançável digitando a URL.
    ///
    /// A Key PRECISA ser igual ao nome da rota no manifest.json: App.controller.ts navega com
    /// navTo(item.getKey()). Sem o vínculo em ROLE_MENUS o item não aparece para ninguém.
    ///
    /// Order 14 porque 1 a 13 já estão ocupados em "sales" (13 é o Painel de Cargas). Fica no fim
    /// do grupo para não renumerar itens existentes.
    /// </summary>
    public partial class AddSalesShipmentsMenu : Migration
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
                        "storageTransactionsSales", "Romaneios de Embarque",
                        "sap-icon://shipping-status", true, false, 14, "sales"
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
                        "3B27D5E1-6A94-4F82-9C10-7D5B8E24A6F3", "ADMIN",
                        "storageTransactionsSales"
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
                keyValues: ["3B27D5E1-6A94-4F82-9C10-7D5B8E24A6F3"]);

            migrationBuilder.DeleteData(
                table: "MENU_ITEMS",
                keyColumn: "Key",
                keyValues: ["storageTransactionsSales"]);
        }
    }
}
