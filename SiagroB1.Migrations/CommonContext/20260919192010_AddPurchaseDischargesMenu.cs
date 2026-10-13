using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.CommonContext
{
    /// <summary>
    /// Menu "Ajustes de Descarga" (troca nota), no grupo "Compras". Order 14 porque 1 a 13 já estão
    /// ocupados em "purchases". A Key PRECISA ser igual ao name da rota no manifest.json
    /// (App.controller navega com navTo(item.getKey())). Sem ROLE_MENUS o item não aparece.
    /// </summary>
    public partial class AddPurchaseDischargesMenu : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "MENU_ITEMS",
                columns: ["Key", "Title", "Icon", "Enabled", "Expanded", "Order", "ParentKey"],
                values: new object[,]
                {
                    { "purchaseDischarges", "Ajustes de Descarga", "sap-icon://folder-blank", true, false, 14, "purchases" },
                });

            migrationBuilder.InsertData(
                table: "ROLE_MENUS",
                columns: ["Id", "RoleCode", "MenuItemKey"],
                values: new object[,]
                {
                    { "68A80A56-787A-4CF4-841D-23786D545F66", "ADMIN", "purchaseDischarges" },
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "ROLE_MENUS",
                keyColumn: "Id",
                keyValue: "68A80A56-787A-4CF4-841D-23786D545F66");

            migrationBuilder.DeleteData(
                table: "MENU_ITEMS",
                keyColumn: "Key",
                keyValue: "purchaseDischarges");
        }
    }
}
