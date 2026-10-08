using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.CommonContext
{
    /// <inheritdoc />
    public partial class AddFiscalDocumentsReportMenus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // A Key PRECISA ser igual ao name da rota no manifest.json do frontend
            // (App.controller.ts navega com navTo(item.getKey())). Sem ROLE_MENUS o item não aparece.
            // salesInvoicesReport já tinha rota (relatório antigo, sem menu) e agora ganha o item.
            migrationBuilder.InsertData(
                table: "MENU_ITEMS",
                columns: ["Key", "Title", "Icon", "Enabled", "Expanded", "Order", "ParentKey", "StandaloneOnly"],
                values: new object[,]
                {
                    { "salesInvoicesReport", "Notas de Saída por Período", "sap-icon://folder-blank", true, false, 7, "reports", false },
                    { "purchaseInvoicesReport", "Notas de Entrada por Período", "sap-icon://folder-blank", true, false, 8, "reports", false },
                    { "salesInvoiceItemsReport", "Itens dos Documentos de Saída", "sap-icon://folder-blank", true, false, 9, "reports", false },
                    { "purchaseInvoiceItemsReport", "Itens dos Documentos de Entrada", "sap-icon://folder-blank", true, false, 10, "reports", false },
                    { "salesReturnsReport", "Devoluções de Venda", "sap-icon://folder-blank", true, false, 11, "reports", false },
                });

            migrationBuilder.InsertData(
                table: "ROLE_MENUS",
                columns: ["Id", "RoleCode", "MenuItemKey"],
                values: new object[,]
                {
                    { "6B1E2F47-0C3A-4D58-9E21-7A4C5B8D3E01", "ADMIN", "salesInvoicesReport" },
                    { "6B1E2F47-0C3A-4D58-9E21-7A4C5B8D3E02", "ADMIN", "purchaseInvoicesReport" },
                    { "6B1E2F47-0C3A-4D58-9E21-7A4C5B8D3E03", "ADMIN", "salesInvoiceItemsReport" },
                    { "6B1E2F47-0C3A-4D58-9E21-7A4C5B8D3E04", "ADMIN", "purchaseInvoiceItemsReport" },
                    { "6B1E2F47-0C3A-4D58-9E21-7A4C5B8D3E05", "ADMIN", "salesReturnsReport" },
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "ROLE_MENUS",
                keyColumn: "Id",
                keyValues:
                [
                    "6B1E2F47-0C3A-4D58-9E21-7A4C5B8D3E01", "6B1E2F47-0C3A-4D58-9E21-7A4C5B8D3E02",
                    "6B1E2F47-0C3A-4D58-9E21-7A4C5B8D3E03", "6B1E2F47-0C3A-4D58-9E21-7A4C5B8D3E04",
                    "6B1E2F47-0C3A-4D58-9E21-7A4C5B8D3E05",
                ]);

            migrationBuilder.DeleteData(
                table: "MENU_ITEMS",
                keyColumn: "Key",
                keyValues:
                [
                    "salesInvoicesReport", "purchaseInvoicesReport", "salesInvoiceItemsReport",
                    "purchaseInvoiceItemsReport", "salesReturnsReport",
                ]);
        }
    }
}
