using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.CommonContext
{
    /// <inheritdoc />
    public partial class AddEmailSettingsMenu : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // A Key PRECISA ser igual ao name da rota no manifest.json do frontend. Só STANDALONE.
            migrationBuilder.InsertData(
                table: "MENU_ITEMS",
                columns: ["Key", "Title", "Icon", "Enabled", "Expanded", "Order", "ParentKey", "StandaloneOnly"],
                values: new object[,]
                {
                    { "emailSettings", "Configuração de e-mail", "sap-icon://email", true, false, 19, "registers", true },
                });

            migrationBuilder.InsertData(
                table: "ROLE_MENUS",
                columns: ["Id", "RoleCode", "MenuItemKey"],
                values: new object[,]
                {
                    { "A4C81E57-2B63-4D90-8F15-6E3D7A9B0C42", "ADMIN", "emailSettings" },
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(table: "ROLE_MENUS", keyColumn: "Id", keyValue: "A4C81E57-2B63-4D90-8F15-6E3D7A9B0C42");
            migrationBuilder.DeleteData(table: "MENU_ITEMS", keyColumn: "Key", keyValue: "emailSettings");
        }
    }
}
