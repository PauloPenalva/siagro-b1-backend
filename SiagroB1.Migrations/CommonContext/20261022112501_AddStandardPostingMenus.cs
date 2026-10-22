using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.CommonContext
{
    /// <summary>
    /// Tela Lançamento Padrão (CT5) no módulo Contabilidade, depois do Balancete (9). Só STANDALONE, concedida ao
    /// ADMIN. A Key precisa ser igual ao name da rota no manifest.json do frontend.
    /// </summary>
    public partial class AddStandardPostingMenus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "MENU_ITEMS",
                columns: ["Key", "Title", "Icon", "Enabled", "Expanded", "Order", "ParentKey", "StandaloneOnly"],
                values: new object[] { "standardPostings", "Lançamento Padrão", "sap-icon://process", true, false, 10, "accounting", true });

            migrationBuilder.InsertData(
                table: "ROLE_MENUS",
                columns: ["Id", "RoleCode", "MenuItemKey"],
                values: new object[] { "A1C7D2E3-5F60-4A11-8B21-3C4D5E6F7A06", "ADMIN", "standardPostings" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM ROLE_MENUS WHERE MenuItemKey = 'standardPostings'");
            migrationBuilder.Sql("DELETE FROM MENU_ITEMS WHERE [Key] = 'standardPostings'");
        }
    }
}
