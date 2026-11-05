using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.CommonContext
{
    /// <summary>
    /// Módulo "Mercado" (cotações) no menu superior, Order 11 (1 a 10 já ocupados). As Keys PRECISAM ser iguais
    /// ao name das rotas do manifest.json do frontend (App.controller.ts navega com navTo(item.getKey())).
    /// A migration vale para todos os bancos; o MenuService esconde a árvore "market" quando
    /// Modules:Market:Enabled está desligado.
    /// </summary>
    public partial class AddMarketMenu : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "MENU_ITEMS",
                columns: ["Key", "Title", "Icon", "Enabled", "Expanded", "Order", "ParentKey"],
                values: new object[,]
                {
                    { "market", "Mercado", "sap-icon://folder-blank", true, false, 11, null },
                    { "marketQuotesTerminal", "Terminal de Cotações", "sap-icon://folder-blank", true, false, 1, "market" },
                    { "marketInstruments", "Instrumentos", "sap-icon://folder-blank", true, false, 2, "market" },
                    { "marketCmaSettings", "Integração CMA", "sap-icon://folder-blank", true, false, 3, "market" },
                });

            migrationBuilder.InsertData(
                table: "ROLE_MENUS",
                columns: ["Id", "RoleCode", "MenuItemKey"],
                values: new object[,]
                {
                    { "5E3A9C71-0B4D-4F82-A6E1-7C29D8B3F054", "ADMIN", "market" },
                    { "A81F6D20-3C57-4E9B-8D14-62B0E7A5C913", "ADMIN", "marketQuotesTerminal" },
                    { "C47B0E95-81D2-4A36-9F58-1E6D3A2B7C80", "ADMIN", "marketInstruments" },
                    { "13D9F5A8-6E04-4B7C-A2D1-9B85C0E4F726", "ADMIN", "marketCmaSettings" },
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var id in new[]
            {
                "5E3A9C71-0B4D-4F82-A6E1-7C29D8B3F054", "A81F6D20-3C57-4E9B-8D14-62B0E7A5C913",
                "C47B0E95-81D2-4A36-9F58-1E6D3A2B7C80", "13D9F5A8-6E04-4B7C-A2D1-9B85C0E4F726"
            })
            {
                migrationBuilder.DeleteData(table: "ROLE_MENUS", keyColumn: "Id", keyValue: id);
            }

            // Filhos antes do pai (FK ParentKey).
            foreach (var key in new[] { "marketCmaSettings", "marketInstruments", "marketQuotesTerminal", "market" })
            {
                migrationBuilder.DeleteData(table: "MENU_ITEMS", keyColumn: "Key", keyValue: key);
            }
        }
    }
}
