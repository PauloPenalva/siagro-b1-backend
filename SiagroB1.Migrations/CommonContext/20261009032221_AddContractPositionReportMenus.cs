using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.CommonContext
{
    /// <inheritdoc />
    public partial class AddContractPositionReportMenus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // A Key PRECISA ser igual ao name da rota no manifest.json do frontend
            // (App.controller.ts navega com navTo(item.getKey())). Sem ROLE_MENUS o item não aparece.
            // Os relatórios antigos de contratos (PurchaseContractsByItem/SalesContractsByItem)
            // continuam no menu.
            migrationBuilder.InsertData(
                table: "MENU_ITEMS",
                columns: ["Key", "Title", "Icon", "Enabled", "Expanded", "Order", "ParentKey", "StandaloneOnly"],
                values: new object[,]
                {
                    { "contractPositionReport", "Contratos — Compra x Venda", "sap-icon://folder-blank", true, false, 16, "reports", false },
                    { "contractMonthlyPositionReport", "Posição Comprado x Vendido por Mês", "sap-icon://folder-blank", true, false, 17, "reports", false },
                });

            migrationBuilder.InsertData(
                table: "ROLE_MENUS",
                columns: ["Id", "RoleCode", "MenuItemKey"],
                values: new object[,]
                {
                    { "3E8B1C47-6A2D-4F95-B7C1-2D9E5A8F0B01", "ADMIN", "contractPositionReport" },
                    { "3E8B1C47-6A2D-4F95-B7C1-2D9E5A8F0B02", "ADMIN", "contractMonthlyPositionReport" },
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "ROLE_MENUS",
                keyColumn: "Id",
                keyValues: ["3E8B1C47-6A2D-4F95-B7C1-2D9E5A8F0B01", "3E8B1C47-6A2D-4F95-B7C1-2D9E5A8F0B02"]);

            migrationBuilder.DeleteData(
                table: "MENU_ITEMS",
                keyColumn: "Key",
                keyValues: ["contractPositionReport", "contractMonthlyPositionReport"]);
        }
    }
}
