using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.CommonContext
{
    /// <inheritdoc />
    public partial class AddMenuItemStandaloneOnly : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "StandaloneOnly",
                table: "MENU_ITEMS",
                type: "bit",
                nullable: false,
                defaultValue: false);

            // A Key PRECISA ser igual ao name da rota no manifest.json do frontend
            // (App.controller.ts navega com navTo(item.getKey())). Sem ROLE_MENUS o item não aparece.
            migrationBuilder.InsertData(
                table: "MENU_ITEMS",
                columns: ["Key", "Title", "Icon", "Enabled", "Expanded", "Order", "ParentKey", "StandaloneOnly"],
                values: new object[,]
                {
                    { "paymentConditions", "Condições de Pagamento", "sap-icon://payment-approval", true, false, 16, "registers", true },
                    { "nfeSettings", "Configuração da NF-e", "sap-icon://action-settings", true, false, 17, "registers", true },
                });

            migrationBuilder.InsertData(
                table: "ROLE_MENUS",
                columns: ["Id", "RoleCode", "MenuItemKey"],
                values: new object[,]
                {
                    { "8E5B2C71-4A39-4F0D-9C61-2D7A3B5E9F14", "ADMIN", "paymentConditions" },
                    { "3F9D6A20-7C84-4E1B-A5D2-6B0E8C47F9A3", "ADMIN", "nfeSettings" },
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "ROLE_MENUS",
                keyColumn: "Id",
                keyValues: ["8E5B2C71-4A39-4F0D-9C61-2D7A3B5E9F14", "3F9D6A20-7C84-4E1B-A5D2-6B0E8C47F9A3"]);

            migrationBuilder.DeleteData(
                table: "MENU_ITEMS",
                keyColumn: "Key",
                keyValues: ["paymentConditions", "nfeSettings"]);
            migrationBuilder.DropColumn(
                name: "StandaloneOnly",
                table: "MENU_ITEMS");
        }
    }
}
