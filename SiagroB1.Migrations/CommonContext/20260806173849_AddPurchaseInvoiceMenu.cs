using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.CommonContext
{
    /// <summary>
    /// Menu do Documento de Entrada, no grupo "Compras" — é onde a NF de fornecedor, a venda futura
    /// do produtor e suas remessas pertencem. Substitui o item "Devoluções de Clientes", que ficava
    /// em "Vendas": a devolução deixou de ser rotina própria e virou um TIPO deste documento.
    ///
    /// A Key PRECISA ser igual ao nome da rota no manifest.json: App.controller.ts navega com
    /// navTo(item.getKey()). Sem o vínculo em ROLE_MENUS o item não aparece para ninguém.
    ///
    /// Order 9 porque 1 a 8 já estão ocupados em "purchases" (7 é storageEntryTransaction, 8 é a
    /// aprovação de fixação de preço).
    /// </summary>
    public partial class AddPurchaseInvoiceMenu : Migration
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
                        "purchaseInvoices", "Documentos de Entrada",
                        "sap-icon://journey-arrive", true, false, 9, "purchases"
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
                        "3F7A1C08-9B24-4E6D-8A15-2C90D4B7E631", "ADMIN",
                        "purchaseInvoices"
                    },
                });

            // A devolução sai de "Vendas". Junto vai a colisão de Order 8 que ela tinha com
            // salesContractsShipmentRelease, que deixava a ordenação entre as duas indefinida.
            // O ROLE_MENUS sai PRIMEIRO: tem FK para MENU_ITEMS.
            migrationBuilder.DeleteData(
                table: "ROLE_MENUS",
                keyColumn: "Id",
                keyValues: ["B1D46F92-7C35-4A08-9E27-5D3F8A1C0B64"]);

            migrationBuilder.DeleteData(
                table: "MENU_ITEMS",
                keyColumn: "Key",
                keyValues: ["customerReturns"]);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
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
                        "customerReturns", "Devoluções de Clientes",
                        "sap-icon://journey-arrive", true, false, 8, "sales"
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
                        "B1D46F92-7C35-4A08-9E27-5D3F8A1C0B64", "ADMIN",
                        "customerReturns"
                    },
                });

            migrationBuilder.DeleteData(
                table: "ROLE_MENUS",
                keyColumn: "Id",
                keyValues: ["3F7A1C08-9B24-4E6D-8A15-2C90D4B7E631"]);

            migrationBuilder.DeleteData(
                table: "MENU_ITEMS",
                keyColumn: "Key",
                keyValues: ["purchaseInvoices"]);
        }
    }
}
