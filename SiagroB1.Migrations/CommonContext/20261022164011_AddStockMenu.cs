using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.CommonContext
{
    /// <summary>
    /// Novo menu superior "Estoque" (só STANDALONE) com as telas de estoque fiscal que estavam em "Fiscal",
    /// renomeadas para os nomes dos livros. Keys inalteradas: são o name da rota no manifest do frontend.
    /// </summary>
    public partial class AddStockMenu : Migration
    {
        private const string RoleMenuId = "CF5FD46E-8463-400F-9795-CCA8303DE9FE";

        // Key, título novo, ordem nova no Estoque, título antigo, ordem antiga no Fiscal
        private static readonly (string Key, string Title, int Order, string OldTitle, int OldOrder)[] Items =
        [
            ("fiscalInventoryOpeningBalances", "Saldo Inicial do Estoque", 1, "Saldo Inicial do Estoque", 6),
            ("fiscalKardex", "Registro Kardex", 2, "Kardex Fiscal", 7),
            ("fiscalStockAdjustments", "Ajuste de Estoque", 3, "Ajustes de Estoque Fiscal", 9),
            ("fiscalStockPeriods", "Fechamento de Estoque Mensal", 4, "Fechamento do Estoque Fiscal", 10),
            ("fiscalInventory", "Registro de Inventário Modelo P7", 5, "Inventário em Data", 11),
        ];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Order 9: logo depois de Fiscal/Contabilidade (8); Relatórios segue em 999.
            migrationBuilder.InsertData(
                table: "MENU_ITEMS",
                columns: ["Key", "Title", "Icon", "Enabled", "Expanded", "Order", "ParentKey", "StandaloneOnly"],
                values: new object[] { "stock", "Estoque", "sap-icon://folder-blank", true, false, 9, null, true });

            migrationBuilder.InsertData(
                table: "ROLE_MENUS",
                columns: ["Id", "RoleCode", "MenuItemKey"],
                values: new object[] { RoleMenuId, "ADMIN", "stock" });

            foreach (var item in Items)
                Move(migrationBuilder, item.Key, item.Title, "stock", item.Order);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var item in Items)
                Move(migrationBuilder, item.Key, item.OldTitle, "fiscal", item.OldOrder);

            // ROLE_MENUS primeiro: tem FK para MENU_ITEMS.
            migrationBuilder.DeleteData(table: "ROLE_MENUS", keyColumn: "Id", keyValue: RoleMenuId);
            migrationBuilder.DeleteData(table: "MENU_ITEMS", keyColumn: "Key", keyValue: "stock");
        }

        private static void Move(MigrationBuilder migrationBuilder, string key, string title, string parent, int order) =>
            migrationBuilder.UpdateData(
                table: "MENU_ITEMS",
                keyColumn: "Key",
                keyValue: key,
                columns: ["Title", "ParentKey", "Order"],
                values: new object[] { title, parent, order });
    }
}
