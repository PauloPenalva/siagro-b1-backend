using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.CommonContext
{
    /// <summary>
    /// Menu da fila de aprovação de washouts do contrato de compra, no grupo "Compras". Order 13
    /// porque 1 a 12 já estão ocupados em "purchases" (10 a 12 são da Conferência de Saldo).
    ///
    /// A Key PRECISA ser igual ao name da rota no manifest.json do frontend:
    /// App.controller.ts navega com navTo(item.getKey()). Sem ROLE_MENUS o item não aparece.
    /// </summary>
    public partial class AddPurchaseContractWashoutApprovalMenu : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "MENU_ITEMS",
                columns: ["Key", "Title", "Icon", "Enabled", "Expanded", "Order", "ParentKey"],
                values: new object[,]
                {
                    { "purchaseContractsWashoutApproval", "Aprovação de Washouts", "sap-icon://folder-blank", true, false, 13, "purchases" },
                });

            migrationBuilder.InsertData(
                table: "ROLE_MENUS",
                columns: ["Id", "RoleCode", "MenuItemKey"],
                values: new object[,]
                {
                    { "5E6F7A8B-9C0D-4E1F-8A2B-3C4D5E6F7A95", "ADMIN", "purchaseContractsWashoutApproval" },
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(
                table: "ROLE_MENUS",
                keyColumn: "Id",
                keyValue: "5E6F7A8B-9C0D-4E1F-8A2B-3C4D5E6F7A95");

            migrationBuilder.DeleteData(
                table: "MENU_ITEMS",
                keyColumn: "Key",
                keyValue: "purchaseContractsWashoutApproval");
        }
    }
}
