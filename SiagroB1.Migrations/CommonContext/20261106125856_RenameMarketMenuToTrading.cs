using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.CommonContext
{
    /// <summary>
    /// O módulo passa a se chamar "Trading" no que o usuário lê (decisão de 06/11/2026): título do menu superior,
    /// do item "Parâmetros" e descrições das permissões e dos papéis. As Keys, os Codes e os identificadores do
    /// código continuam "market"/"MARKET_*". Cada UPDATE só troca o texto que ainda está como a migration original
    /// gravou — se o cliente já renomeou algo à mão, fica como ele deixou. O Down desfaz do mesmo jeito.
    /// </summary>
    public partial class RenameMarketMenuToTrading : Migration
    {
        private static readonly (string Table, string KeyColumn, string Key, string TextColumn, string Old, string New)[] Texts =
        [
            ("MENU_ITEMS", "[Key]", "market", "Title", "Mercado", "Trading"),
            ("MENU_ITEMS", "[Key]", "marketTradingSettings", "Title", "Parâmetros do Mercado", "Parâmetros de Trading"),
            ("PERMISSIONS", "Code", "MARKET_MANAGE", "Description", "Mercado: manter cadastros e cotações", "Trading: manter cadastros e cotações"),
            ("PERMISSIONS", "Code", "MARKET_TICKET_APPROVE", "Description", "Mercado: aprovar boletas", "Trading: aprovar boletas"),
            ("PERMISSIONS", "Code", "MARKET_BOARD_APPROVE", "Description", "Mercado: aprovar boletas na alçada da diretoria", "Trading: aprovar boletas na alçada da diretoria"),
            ("ROLES", "Code", "MARKET_TRADER", "Description", "Trader do Mercado", "Trader"),
            ("ROLES", "Code", "MARKET_BOARD", "Description", "Diretoria (Mercado)", "Diretoria (Trading)"),
        ];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            foreach (var t in Texts)
                migrationBuilder.Sql(Update(t.Table, t.KeyColumn, t.Key, t.TextColumn, t.Old, t.New));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var t in Texts)
                migrationBuilder.Sql(Update(t.Table, t.KeyColumn, t.Key, t.TextColumn, t.New, t.Old));
        }

        private static string Update(string table, string keyColumn, string key, string textColumn, string from, string to) =>
            $"UPDATE {table} SET {textColumn} = N'{to}' WHERE {keyColumn} = '{key}' AND {textColumn} = N'{from}';";
    }
}
