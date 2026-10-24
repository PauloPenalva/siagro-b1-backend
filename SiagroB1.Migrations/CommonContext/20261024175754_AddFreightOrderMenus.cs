using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.CommonContext
{
    /// <summary>
    /// Itens "Ordens de Frete" e "Faturas de Frete" no módulo TMS (só STANDALONE). Todo perfil que já vê
    /// Conhecimentos de Transporte passa a ver os dois (o ADMIN com Id fixo).
    /// </summary>
    public partial class AddFreightOrderMenus : Migration
    {
        private const string OrdersRoleMenuId = "B3E5A7C1-2D48-4F96-A013-5C8E7D1F9B24";
        private const string InvoicesRoleMenuId = "E70C94D2-8A15-4B3E-9F67-1A2D5C8B30E9";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.InsertData(
                table: "MENU_ITEMS",
                columns: ["Key", "Title", "Icon", "Enabled", "Expanded", "Order", "ParentKey", "StandaloneOnly"],
                values: new object[] { "freightOrders", "Ordens de Frete", "sap-icon://shipping-status", true, false, 6, "tms", true });

            migrationBuilder.InsertData(
                table: "MENU_ITEMS",
                columns: ["Key", "Title", "Icon", "Enabled", "Expanded", "Order", "ParentKey", "StandaloneOnly"],
                values: new object[] { "freightInvoices", "Faturas de Frete", "sap-icon://shipping-status", true, false, 7, "tms", true });

            migrationBuilder.InsertData(
                table: "ROLE_MENUS",
                columns: ["Id", "RoleCode", "MenuItemKey"],
                values: new object[,]
                {
                    { OrdersRoleMenuId, "ADMIN", "freightOrders" },
                    { InvoicesRoleMenuId, "ADMIN", "freightInvoices" }
                });

            foreach (var key in new[] { "freightOrders", "freightInvoices" })
                migrationBuilder.Sql($@"
INSERT INTO ROLE_MENUS (Id, RoleCode, MenuItemKey)
SELECT NEWID(), r.RoleCode, '{key}'
FROM (SELECT DISTINCT RoleCode FROM ROLE_MENUS WHERE MenuItemKey = 'transportDocuments') r
WHERE NOT EXISTS (SELECT 1 FROM ROLE_MENUS x WHERE x.RoleCode = r.RoleCode AND x.MenuItemKey = '{key}');");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // ROLE_MENUS primeiro: tem FK para MENU_ITEMS.
            migrationBuilder.Sql("DELETE FROM ROLE_MENUS WHERE MenuItemKey IN ('freightOrders', 'freightInvoices');");
            migrationBuilder.Sql("DELETE FROM MENU_ITEMS WHERE [Key] IN ('freightOrders', 'freightInvoices');");
        }
    }
}
