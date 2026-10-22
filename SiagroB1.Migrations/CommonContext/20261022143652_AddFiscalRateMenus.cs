using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.CommonContext
{
    /// <summary>
    /// Alíquotas IBS/CBS e Alíquotas de contribuição social ganham entrada própria no menu Fiscal, logo depois de
    /// Naturezas de Operação (saíram como botões da lista de naturezas). Só STANDALONE, concedidas ao ADMIN. A Key é o
    /// name da rota no manifest.json do frontend.
    /// </summary>
    public partial class AddFiscalRateMenus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Abre as posições 2 e 3, logo depois de Naturezas de Operação (1).
            migrationBuilder.Sql("UPDATE MENU_ITEMS SET [Order] = [Order] + 2 WHERE ParentKey = 'fiscal' AND [Order] >= 2");

            migrationBuilder.InsertData(
                table: "MENU_ITEMS",
                columns: ["Key", "Title", "Icon", "Enabled", "Expanded", "Order", "ParentKey", "StandaloneOnly"],
                values: new object[] { "ibsCbsRates", "Alíquotas IBS/CBS", "sap-icon://simulate", true, false, 2, "fiscal", true });
            migrationBuilder.InsertData(
                table: "MENU_ITEMS",
                columns: ["Key", "Title", "Icon", "Enabled", "Expanded", "Order", "ParentKey", "StandaloneOnly"],
                values: new object[] { "ruralContributionRates", "Alíquotas de Contribuição Social", "sap-icon://simulate", true, false, 3, "fiscal", true });

            migrationBuilder.InsertData(
                table: "ROLE_MENUS",
                columns: ["Id", "RoleCode", "MenuItemKey"],
                values: new object[] { "D4FA0516-8293-4D44-BE54-6F7A8B9C0D01", "ADMIN", "ibsCbsRates" });
            migrationBuilder.InsertData(
                table: "ROLE_MENUS",
                columns: ["Id", "RoleCode", "MenuItemKey"],
                values: new object[] { "D4FA0516-8293-4D44-BE54-6F7A8B9C0D02", "ADMIN", "ruralContributionRates" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM ROLE_MENUS WHERE MenuItemKey IN ('ibsCbsRates', 'ruralContributionRates')");
            migrationBuilder.Sql("DELETE FROM MENU_ITEMS WHERE [Key] IN ('ibsCbsRates', 'ruralContributionRates')");
            migrationBuilder.Sql("UPDATE MENU_ITEMS SET [Order] = [Order] - 2 WHERE ParentKey = 'fiscal' AND [Order] >= 4");
        }
    }
}
