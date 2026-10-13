using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.CommonContext
{
    /// <summary>
    /// Conciliação Fiscal de Compras (spec 2026-10-13): a tela no grupo "Compras" e o relatório em "Relatórios". A Key
    /// PRECISA ser igual ao name da rota no manifest.json (App.controller navega com navTo(item.getKey())). Sem
    /// ROLE_MENUS o item não aparece.
    /// </summary>
    public partial class AddPurchaseFiscalReconciliationMenus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "MENU_ITEMS",
                columns: ["Key", "Title", "Icon", "Enabled", "Expanded", "Order", "ParentKey"],
                values: new object[,]
                {
                    { "purchaseFiscalReconciliations", "Conciliação Fiscal de Compras", "sap-icon://folder-blank", true, false, 15, "purchases" },
                    { "purchaseFiscalReconciliationReport", "Entregas × NF do Fornecedor", "sap-icon://folder-blank", true, false, 21, "reports" },
                });

            migrationBuilder.InsertData(
                table: "ROLE_MENUS",
                columns: ["Id", "RoleCode", "MenuItemKey"],
                values: new object[,]
                {
                    { "3C1E5A7B-2D94-4F6A-9B08-6E2F1A4C7D35", "ADMIN", "purchaseFiscalReconciliations" },
                    { "8F2B6D41-7A3C-4E59-B1D0-2C9E5F7A3B68", "ADMIN", "purchaseFiscalReconciliationReport" },
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(table: "ROLE_MENUS", keyColumn: "Id", keyValue: "3C1E5A7B-2D94-4F6A-9B08-6E2F1A4C7D35");
            migrationBuilder.DeleteData(table: "ROLE_MENUS", keyColumn: "Id", keyValue: "8F2B6D41-7A3C-4E59-B1D0-2C9E5F7A3B68");
            migrationBuilder.DeleteData(table: "MENU_ITEMS", keyColumn: "Key", keyValue: "purchaseFiscalReconciliations");
            migrationBuilder.DeleteData(table: "MENU_ITEMS", keyColumn: "Key", keyValue: "purchaseFiscalReconciliationReport");
        }
    }
}
