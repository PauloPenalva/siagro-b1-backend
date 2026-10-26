using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.CommonContext
{
    /// <summary>
    /// Menu da Liberação de Pagamento do contrato de compra (GAC-1188), no grupo "Compras". Order 16
    /// porque 1 a 15 já estão ocupados em "purchases".
    ///
    /// A Key PRECISA ser igual ao name da rota no manifest.json do frontend:
    /// App.controller.ts navega com navTo(item.getKey()). Sem ROLE_MENUS o item não aparece.
    /// </summary>
    public partial class AddPaymentReleaseMenu : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "MENU_ITEMS",
                columns: ["Key", "Title", "Icon", "Enabled", "Expanded", "Order", "ParentKey"],
                values: new object[,]
                {
                    { "paymentReleases", "Liberação de Pagamento", "sap-icon://folder-blank", true, false, 16, "purchases" },
                });

            migrationBuilder.InsertData(
                table: "ROLE_MENUS",
                columns: ["Id", "RoleCode", "MenuItemKey"],
                values: new object[,]
                {
                    { "B05AC82B-1D5C-49FF-810F-8EC85BBBE8F6", "ADMIN", "paymentReleases" },
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "ROLE_MENUS",
                keyColumn: "Id",
                keyValue: "B05AC82B-1D5C-49FF-810F-8EC85BBBE8F6");

            migrationBuilder.DeleteData(
                table: "MENU_ITEMS",
                keyColumn: "Key",
                keyValue: "paymentReleases");
        }
    }
}
