using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.CommonContext
{
    /// <summary>
    /// Tela Contabilização Off-line no módulo Contabilidade, depois do Lançamento Padrão (10). Só STANDALONE,
    /// concedida ao ADMIN. A Key precisa ser igual ao name da rota no manifest.json do frontend.
    /// </summary>
    public partial class AddOfflinePostingMenu : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "MENU_ITEMS",
                columns: ["Key", "Title", "Icon", "Enabled", "Expanded", "Order", "ParentKey", "StandaloneOnly"],
                values: new object[] { "offlinePosting", "Contabilização Off-line", "sap-icon://synchronize", true, false, 11, "accounting", true });

            migrationBuilder.InsertData(
                table: "ROLE_MENUS",
                columns: ["Id", "RoleCode", "MenuItemKey"],
                values: new object[] { "B2D8E3F4-6071-4B22-9C32-4D5E6F7A8B07", "ADMIN", "offlinePosting" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM ROLE_MENUS WHERE MenuItemKey = 'offlinePosting'");
            migrationBuilder.Sql("DELETE FROM MENU_ITEMS WHERE [Key] = 'offlinePosting'");
        }
    }
}
