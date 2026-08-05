using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.CommonContext
{
    /// <summary>
    /// Menu das devoluções emitidas pelo cliente, no grupo "Vendas" — ao lado dos documentos
    /// de saída e da conferência de entregas, que é onde a quebra que elas espelham nasce.
    ///
    /// A Key PRECISA ser igual ao nome da rota no manifest.json: App.controller.ts navega com
    /// navTo(item.getKey()). Sem o vínculo em ROLE_MENUS o item não aparece para ninguém.
    /// </summary>
    public partial class AddCustomerReturnMenu : Migration
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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "ROLE_MENUS",
                keyColumn: "Id",
                keyValues: ["B1D46F92-7C35-4A08-9E27-5D3F8A1C0B64"]);

            migrationBuilder.DeleteData(
                table: "MENU_ITEMS",
                keyColumn: "Key",
                keyValues: ["customerReturns"]);
        }
    }
}
