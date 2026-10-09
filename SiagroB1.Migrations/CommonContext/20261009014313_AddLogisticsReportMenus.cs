using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.CommonContext
{
    /// <inheritdoc />
    public partial class AddLogisticsReportMenus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // A Key PRECISA ser igual ao name da rota no manifest.json do frontend
            // (App.controller.ts navega com navTo(item.getKey())). Sem ROLE_MENUS o item não aparece.
            // "Romaneios de Venda" é relatório novo; o antigo "Romaneios de Saída"
            // (storageTransactionsShipmentsReport) continua no menu.
            migrationBuilder.InsertData(
                table: "MENU_ITEMS",
                columns: ["Key", "Title", "Icon", "Enabled", "Expanded", "Order", "ParentKey", "StandaloneOnly"],
                values: new object[,]
                {
                    { "shipmentLoadsReport", "Cargas por Período", "sap-icon://folder-blank", true, false, 12, "reports", false },
                    { "salesShipmentReleasesReport", "Liberações de Venda", "sap-icon://folder-blank", true, false, 13, "reports", false },
                    { "shipmentReleasesReport", "Liberações de Compra", "sap-icon://folder-blank", true, false, 14, "reports", false },
                    { "salesShipmentsReport", "Romaneios de Venda", "sap-icon://folder-blank", true, false, 15, "reports", false },
                });

            migrationBuilder.InsertData(
                table: "ROLE_MENUS",
                columns: ["Id", "RoleCode", "MenuItemKey"],
                values: new object[,]
                {
                    { "7C2F3A58-1D4B-4E69-8F32-8B5D6C9E4F01", "ADMIN", "shipmentLoadsReport" },
                    { "7C2F3A58-1D4B-4E69-8F32-8B5D6C9E4F02", "ADMIN", "salesShipmentReleasesReport" },
                    { "7C2F3A58-1D4B-4E69-8F32-8B5D6C9E4F03", "ADMIN", "shipmentReleasesReport" },
                    { "7C2F3A58-1D4B-4E69-8F32-8B5D6C9E4F04", "ADMIN", "salesShipmentsReport" },
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
                    "7C2F3A58-1D4B-4E69-8F32-8B5D6C9E4F01", "7C2F3A58-1D4B-4E69-8F32-8B5D6C9E4F02",
                    "7C2F3A58-1D4B-4E69-8F32-8B5D6C9E4F03", "7C2F3A58-1D4B-4E69-8F32-8B5D6C9E4F04",
                ]);

            migrationBuilder.DeleteData(
                table: "MENU_ITEMS",
                keyColumn: "Key",
                keyValues:
                [
                    "shipmentLoadsReport", "salesShipmentReleasesReport", "shipmentReleasesReport",
                    "salesShipmentsReport",
                ]);
        }
    }
}
