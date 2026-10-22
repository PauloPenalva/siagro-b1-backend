using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.CommonContext
{
    /// <summary>
    /// Fase 4 da contabilidade: Apuração de Resultado, Visão Gerencial e os relatórios Diário, DRE e Balanço no módulo
    /// Contabilidade. Só STANDALONE, concedidos ao ADMIN. A Key é o name da rota no manifest.json do frontend.
    /// </summary>
    public partial class AddAccountingClosingMenus : Migration
    {
        private static readonly (string Key, string Title, string Icon, int Order, string RoleMenuId)[] Menus =
        [
            ("resultClosing", "Apuração de Resultado", "sap-icon://balance", 12, "C3E9F405-7182-4C33-AD43-5E6F7A8B9C01"),
            ("managementViews", "Visão Gerencial", "sap-icon://tree", 13, "C3E9F405-7182-4C33-AD43-5E6F7A8B9C02"),
            ("accountingJournalReport", "Diário", "sap-icon://course-book", 14, "C3E9F405-7182-4C33-AD43-5E6F7A8B9C03"),
            ("incomeStatementReport", "DRE", "sap-icon://line-chart", 15, "C3E9F405-7182-4C33-AD43-5E6F7A8B9C04"),
            ("balanceSheetReport", "Balanço", "sap-icon://compare", 16, "C3E9F405-7182-4C33-AD43-5E6F7A8B9C05"),
        ];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            foreach (var m in Menus)
            {
                migrationBuilder.InsertData(
                    table: "MENU_ITEMS",
                    columns: ["Key", "Title", "Icon", "Enabled", "Expanded", "Order", "ParentKey", "StandaloneOnly"],
                    values: new object[] { m.Key, m.Title, m.Icon, true, false, m.Order, "accounting", true });
                migrationBuilder.InsertData(
                    table: "ROLE_MENUS",
                    columns: ["Id", "RoleCode", "MenuItemKey"],
                    values: new object[] { m.RoleMenuId, "ADMIN", m.Key });
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var m in Menus)
            {
                migrationBuilder.Sql($"DELETE FROM ROLE_MENUS WHERE MenuItemKey = '{m.Key}'");
                migrationBuilder.Sql($"DELETE FROM MENU_ITEMS WHERE [Key] = '{m.Key}'");
            }
        }
    }
}
