using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.CommonContext
{
    /// <summary>
    /// Telas do núcleo contábil (fase 1) no módulo Contabilidade, depois dos cadastros (1-4). Só STANDALONE.
    /// A Key PRECISA ser igual ao name da rota no manifest.json do frontend; sem ROLE_MENUS o item não aparece.
    /// </summary>
    public partial class AddAccountingCoreMenus : Migration
    {
        private static readonly string[] Keys =
        [
            "accountingSettings", "accountingPeriods", "journalEntries", "accountingLedgerReport",
            "accountingTrialBalanceReport",
        ];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "MENU_ITEMS",
                columns: ["Key", "Title", "Icon", "Enabled", "Expanded", "Order", "ParentKey", "StandaloneOnly"],
                values: new object[,]
                {
                    { "accountingSettings", "Configurações Contábeis", "sap-icon://action-settings", true, false, 5, "accounting", true },
                    { "accountingPeriods", "Calendário Contábil", "sap-icon://calendar", true, false, 6, "accounting", true },
                    { "journalEntries", "Lançamentos Contábeis", "sap-icon://journey-arrive", true, false, 7, "accounting", true },
                    { "accountingLedgerReport", "Razão", "sap-icon://customer-financial-fact-sheet", true, false, 8, "accounting", true },
                    { "accountingTrialBalanceReport", "Balancete", "sap-icon://table-view", true, false, 9, "accounting", true },
                });

            migrationBuilder.InsertData(
                table: "ROLE_MENUS",
                columns: ["Id", "RoleCode", "MenuItemKey"],
                values: new object[,]
                {
                    { "A1C7D2E3-5F60-4A11-8B21-3C4D5E6F7A01", "ADMIN", "accountingSettings" },
                    { "A1C7D2E3-5F60-4A11-8B21-3C4D5E6F7A02", "ADMIN", "accountingPeriods" },
                    { "A1C7D2E3-5F60-4A11-8B21-3C4D5E6F7A03", "ADMIN", "journalEntries" },
                    { "A1C7D2E3-5F60-4A11-8B21-3C4D5E6F7A04", "ADMIN", "accountingLedgerReport" },
                    { "A1C7D2E3-5F60-4A11-8B21-3C4D5E6F7A05", "ADMIN", "accountingTrialBalanceReport" },
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            var list = string.Join(",", Keys.Select(k => $"'{k}'"));
            migrationBuilder.Sql($"DELETE FROM ROLE_MENUS WHERE MenuItemKey IN ({list})");
            migrationBuilder.Sql($"DELETE FROM MENU_ITEMS WHERE [Key] IN ({list})");
        }
    }
}
