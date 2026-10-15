using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.CommonContext
{
    /// <summary>
    /// Menu da fila de aprovação de washouts do contrato de venda, no grupo "Vendas" (spec 2026-10-15). Order 15
    /// porque 1 a 14 já estão ocupados em "sales" (14 é Romaneios de Embarque).
    ///
    /// A Key PRECISA ser igual ao name da rota no manifest.json do frontend: App.controller.ts navega com
    /// navTo(item.getKey()). Sem ROLE_MENUS o item não aparece.
    /// </summary>
    public partial class AddSalesContractWashoutApprovalMenu : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "MENU_ITEMS",
                columns: ["Key", "Title", "Icon", "Enabled", "Expanded", "Order", "ParentKey"],
                values: new object[,]
                {
                    { "salesContractsWashoutApproval", "Aprovação de Washouts", "sap-icon://folder-blank", true, false, 15, "sales" },
                });

            migrationBuilder.InsertData(
                table: "ROLE_MENUS",
                columns: ["Id", "RoleCode", "MenuItemKey"],
                values: new object[,]
                {
                    { "9A3C5E71-2B4D-4F60-8E1A-7C9D0B2E4F53", "ADMIN", "salesContractsWashoutApproval" },
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(table: "ROLE_MENUS", keyColumn: "Id", keyValue: "9A3C5E71-2B4D-4F60-8E1A-7C9D0B2E4F53");
            migrationBuilder.DeleteData(table: "MENU_ITEMS", keyColumn: "Key", keyValue: "salesContractsWashoutApproval");
        }
    }
}
