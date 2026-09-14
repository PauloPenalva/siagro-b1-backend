using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.CommonContext
{
    /// <summary>
    /// Menus da Conferência de Saldo de Armazém (GAC-1164), no grupo "Compras" (purchases) — a pedido
    /// do usuário, e não em "Armazenagem". Order 10 a 12 porque 1 a 9 já estão ocupados em "purchases"
    /// (o último é Documentos de Entrada).
    ///
    /// A Key de cada item PRECISA ser igual ao name da rota no manifest.json do frontend:
    /// App.controller.ts navega com navTo(item.getKey()).
    /// </summary>
    public partial class AddWarehouseReconciliationMenus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "MENU_ITEMS",
                columns: ["Key", "Title", "Icon", "Enabled", "Expanded", "Order", "ParentKey"],
                values: new object[,]
                {
                    { "warehouseReconciliations", "Conferência de Saldo de Armazém", "sap-icon://folder-blank", true, false, 10, "purchases" },
                    { "warehouseReconciliationsApproval", "Aprovação de Conferências de Saldo", "sap-icon://folder-blank", true, false, 11, "purchases" },
                    { "warehouseReconciliationReasons", "Motivos de Conferência de Saldo", "sap-icon://folder-blank", true, false, 12, "purchases" },
                });

            migrationBuilder.InsertData(
                table: "ROLE_MENUS",
                columns: ["Id", "RoleCode", "MenuItemKey"],
                values: new object[,]
                {
                    { "1A2B3C4D-5E6F-4A7B-8C9D-0E1F2A3B4C51", "ADMIN", "warehouseReconciliations" },
                    { "2B3C4D5E-6F7A-4B8C-9D0E-1F2A3B4C5D62", "ADMIN", "warehouseReconciliationsApproval" },
                    { "3C4D5E6F-7A8B-4C9D-8E1F-2A3B4C5D6E73", "ADMIN", "warehouseReconciliationReasons" },
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
                    "1A2B3C4D-5E6F-4A7B-8C9D-0E1F2A3B4C51",
                    "2B3C4D5E-6F7A-4B8C-9D0E-1F2A3B4C5D62",
                    "3C4D5E6F-7A8B-4C9D-8E1F-2A3B4C5D6E73",
                ]);

            migrationBuilder.DeleteData(
                table: "MENU_ITEMS",
                keyColumn: "Key",
                keyValues: ["warehouseReconciliations", "warehouseReconciliationsApproval", "warehouseReconciliationReasons"]);
        }
    }
}
