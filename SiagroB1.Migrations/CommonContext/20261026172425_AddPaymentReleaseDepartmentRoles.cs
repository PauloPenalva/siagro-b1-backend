using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.CommonContext
{
    /// <summary>
    /// Tela "Liberação por Departamento" (GAC-1188 parte 2): item de menu <c>departmentReleases</c>
    /// (Order 17; 16 é "paymentReleases") e um papel por departamento, cada um ligado à sua
    /// permissão PAYMENT_RELEASE_*. O MenuService só monta a árvore a partir dos ROLE_MENUS do
    /// usuário: um filho sem o pai "purchases" no mesmo papel some do menu, por isso cada papel
    /// também recebe "purchases". ADMIN recebe o novo item. Tudo idempotente.
    /// </summary>
    public partial class AddPaymentReleaseDepartmentRoles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF NOT EXISTS (SELECT 1 FROM MENU_ITEMS WHERE [Key] = 'departmentReleases')
                    INSERT INTO MENU_ITEMS ([Key], Title, Icon, Enabled, Expanded, [Order], ParentKey)
                    VALUES ('departmentReleases', 'Liberação por Departamento', 'sap-icon://folder-blank', 1, 0, 17, 'purchases');
                IF EXISTS (SELECT 1 FROM ROLES WHERE Code = 'ADMIN')
                   AND NOT EXISTS (SELECT 1 FROM ROLE_MENUS WHERE RoleCode = 'ADMIN' AND MenuItemKey = 'departmentReleases')
                    INSERT INTO ROLE_MENUS (Id, RoleCode, MenuItemKey) VALUES (NEWID(), 'ADMIN', 'departmentReleases');
                """);

            migrationBuilder.Sql("""
                IF NOT EXISTS (SELECT 1 FROM ROLES WHERE Code = 'PAYMENT_RELEASE_STOCK')
                    INSERT INTO ROLES (Code, Description) VALUES ('PAYMENT_RELEASE_STOCK', 'Liberação Estoque');
                IF NOT EXISTS (SELECT 1 FROM ROLE_PERMISSIONS WHERE RoleCode = 'PAYMENT_RELEASE_STOCK' AND PermissionCode = 'PAYMENT_RELEASE_STOCK')
                   AND EXISTS (SELECT 1 FROM PERMISSIONS WHERE Code = 'PAYMENT_RELEASE_STOCK')
                    INSERT INTO ROLE_PERMISSIONS (Id, RoleCode, PermissionCode) VALUES (NEWID(), 'PAYMENT_RELEASE_STOCK', 'PAYMENT_RELEASE_STOCK');
                IF NOT EXISTS (SELECT 1 FROM ROLE_MENUS WHERE RoleCode = 'PAYMENT_RELEASE_STOCK' AND MenuItemKey = 'departmentReleases')
                    INSERT INTO ROLE_MENUS (Id, RoleCode, MenuItemKey) VALUES (NEWID(), 'PAYMENT_RELEASE_STOCK', 'departmentReleases');
                IF NOT EXISTS (SELECT 1 FROM ROLE_MENUS WHERE RoleCode = 'PAYMENT_RELEASE_STOCK' AND MenuItemKey = 'purchases')
                    INSERT INTO ROLE_MENUS (Id, RoleCode, MenuItemKey) VALUES (NEWID(), 'PAYMENT_RELEASE_STOCK', 'purchases');
                """);

            migrationBuilder.Sql("""
                IF NOT EXISTS (SELECT 1 FROM ROLES WHERE Code = 'PAYMENT_RELEASE_LEGAL')
                    INSERT INTO ROLES (Code, Description) VALUES ('PAYMENT_RELEASE_LEGAL', 'Liberação Jurídico');
                IF NOT EXISTS (SELECT 1 FROM ROLE_PERMISSIONS WHERE RoleCode = 'PAYMENT_RELEASE_LEGAL' AND PermissionCode = 'PAYMENT_RELEASE_LEGAL')
                   AND EXISTS (SELECT 1 FROM PERMISSIONS WHERE Code = 'PAYMENT_RELEASE_LEGAL')
                    INSERT INTO ROLE_PERMISSIONS (Id, RoleCode, PermissionCode) VALUES (NEWID(), 'PAYMENT_RELEASE_LEGAL', 'PAYMENT_RELEASE_LEGAL');
                IF NOT EXISTS (SELECT 1 FROM ROLE_MENUS WHERE RoleCode = 'PAYMENT_RELEASE_LEGAL' AND MenuItemKey = 'departmentReleases')
                    INSERT INTO ROLE_MENUS (Id, RoleCode, MenuItemKey) VALUES (NEWID(), 'PAYMENT_RELEASE_LEGAL', 'departmentReleases');
                IF NOT EXISTS (SELECT 1 FROM ROLE_MENUS WHERE RoleCode = 'PAYMENT_RELEASE_LEGAL' AND MenuItemKey = 'purchases')
                    INSERT INTO ROLE_MENUS (Id, RoleCode, MenuItemKey) VALUES (NEWID(), 'PAYMENT_RELEASE_LEGAL', 'purchases');
                """);

            migrationBuilder.Sql("""
                IF NOT EXISTS (SELECT 1 FROM ROLES WHERE Code = 'PAYMENT_RELEASE_FISCAL')
                    INSERT INTO ROLES (Code, Description) VALUES ('PAYMENT_RELEASE_FISCAL', 'Liberação Fiscal');
                IF NOT EXISTS (SELECT 1 FROM ROLE_PERMISSIONS WHERE RoleCode = 'PAYMENT_RELEASE_FISCAL' AND PermissionCode = 'PAYMENT_RELEASE_FISCAL')
                   AND EXISTS (SELECT 1 FROM PERMISSIONS WHERE Code = 'PAYMENT_RELEASE_FISCAL')
                    INSERT INTO ROLE_PERMISSIONS (Id, RoleCode, PermissionCode) VALUES (NEWID(), 'PAYMENT_RELEASE_FISCAL', 'PAYMENT_RELEASE_FISCAL');
                IF NOT EXISTS (SELECT 1 FROM ROLE_MENUS WHERE RoleCode = 'PAYMENT_RELEASE_FISCAL' AND MenuItemKey = 'departmentReleases')
                    INSERT INTO ROLE_MENUS (Id, RoleCode, MenuItemKey) VALUES (NEWID(), 'PAYMENT_RELEASE_FISCAL', 'departmentReleases');
                IF NOT EXISTS (SELECT 1 FROM ROLE_MENUS WHERE RoleCode = 'PAYMENT_RELEASE_FISCAL' AND MenuItemKey = 'purchases')
                    INSERT INTO ROLE_MENUS (Id, RoleCode, MenuItemKey) VALUES (NEWID(), 'PAYMENT_RELEASE_FISCAL', 'purchases');
                """);

            migrationBuilder.Sql("""
                IF NOT EXISTS (SELECT 1 FROM ROLES WHERE Code = 'PAYMENT_RELEASE_ROYALTIES')
                    INSERT INTO ROLES (Code, Description) VALUES ('PAYMENT_RELEASE_ROYALTIES', 'Liberação Royalties');
                IF NOT EXISTS (SELECT 1 FROM ROLE_PERMISSIONS WHERE RoleCode = 'PAYMENT_RELEASE_ROYALTIES' AND PermissionCode = 'PAYMENT_RELEASE_ROYALTIES')
                   AND EXISTS (SELECT 1 FROM PERMISSIONS WHERE Code = 'PAYMENT_RELEASE_ROYALTIES')
                    INSERT INTO ROLE_PERMISSIONS (Id, RoleCode, PermissionCode) VALUES (NEWID(), 'PAYMENT_RELEASE_ROYALTIES', 'PAYMENT_RELEASE_ROYALTIES');
                IF NOT EXISTS (SELECT 1 FROM ROLE_MENUS WHERE RoleCode = 'PAYMENT_RELEASE_ROYALTIES' AND MenuItemKey = 'departmentReleases')
                    INSERT INTO ROLE_MENUS (Id, RoleCode, MenuItemKey) VALUES (NEWID(), 'PAYMENT_RELEASE_ROYALTIES', 'departmentReleases');
                IF NOT EXISTS (SELECT 1 FROM ROLE_MENUS WHERE RoleCode = 'PAYMENT_RELEASE_ROYALTIES' AND MenuItemKey = 'purchases')
                    INSERT INTO ROLE_MENUS (Id, RoleCode, MenuItemKey) VALUES (NEWID(), 'PAYMENT_RELEASE_ROYALTIES', 'purchases');
                """);

            migrationBuilder.Sql("""
                IF NOT EXISTS (SELECT 1 FROM ROLES WHERE Code = 'PAYMENT_RELEASE_FINANCIAL')
                    INSERT INTO ROLES (Code, Description) VALUES ('PAYMENT_RELEASE_FINANCIAL', 'Liberação Financeiro');
                IF NOT EXISTS (SELECT 1 FROM ROLE_PERMISSIONS WHERE RoleCode = 'PAYMENT_RELEASE_FINANCIAL' AND PermissionCode = 'PAYMENT_RELEASE_FINANCIAL')
                   AND EXISTS (SELECT 1 FROM PERMISSIONS WHERE Code = 'PAYMENT_RELEASE_FINANCIAL')
                    INSERT INTO ROLE_PERMISSIONS (Id, RoleCode, PermissionCode) VALUES (NEWID(), 'PAYMENT_RELEASE_FINANCIAL', 'PAYMENT_RELEASE_FINANCIAL');
                IF NOT EXISTS (SELECT 1 FROM ROLE_MENUS WHERE RoleCode = 'PAYMENT_RELEASE_FINANCIAL' AND MenuItemKey = 'departmentReleases')
                    INSERT INTO ROLE_MENUS (Id, RoleCode, MenuItemKey) VALUES (NEWID(), 'PAYMENT_RELEASE_FINANCIAL', 'departmentReleases');
                IF NOT EXISTS (SELECT 1 FROM ROLE_MENUS WHERE RoleCode = 'PAYMENT_RELEASE_FINANCIAL' AND MenuItemKey = 'purchases')
                    INSERT INTO ROLE_MENUS (Id, RoleCode, MenuItemKey) VALUES (NEWID(), 'PAYMENT_RELEASE_FINANCIAL', 'purchases');
                """);

            migrationBuilder.Sql("""
                IF NOT EXISTS (SELECT 1 FROM ROLES WHERE Code = 'PAYMENT_RELEASE_STORE')
                    INSERT INTO ROLES (Code, Description) VALUES ('PAYMENT_RELEASE_STORE', 'Liberação Loja');
                IF NOT EXISTS (SELECT 1 FROM ROLE_PERMISSIONS WHERE RoleCode = 'PAYMENT_RELEASE_STORE' AND PermissionCode = 'PAYMENT_RELEASE_STORE')
                   AND EXISTS (SELECT 1 FROM PERMISSIONS WHERE Code = 'PAYMENT_RELEASE_STORE')
                    INSERT INTO ROLE_PERMISSIONS (Id, RoleCode, PermissionCode) VALUES (NEWID(), 'PAYMENT_RELEASE_STORE', 'PAYMENT_RELEASE_STORE');
                IF NOT EXISTS (SELECT 1 FROM ROLE_MENUS WHERE RoleCode = 'PAYMENT_RELEASE_STORE' AND MenuItemKey = 'departmentReleases')
                    INSERT INTO ROLE_MENUS (Id, RoleCode, MenuItemKey) VALUES (NEWID(), 'PAYMENT_RELEASE_STORE', 'departmentReleases');
                IF NOT EXISTS (SELECT 1 FROM ROLE_MENUS WHERE RoleCode = 'PAYMENT_RELEASE_STORE' AND MenuItemKey = 'purchases')
                    INSERT INTO ROLE_MENUS (Id, RoleCode, MenuItemKey) VALUES (NEWID(), 'PAYMENT_RELEASE_STORE', 'purchases');
                """);

            migrationBuilder.Sql("""
                IF NOT EXISTS (SELECT 1 FROM ROLES WHERE Code = 'PAYMENT_RELEASE_BOARD')
                    INSERT INTO ROLES (Code, Description) VALUES ('PAYMENT_RELEASE_BOARD', 'Liberação Diretoria');
                IF NOT EXISTS (SELECT 1 FROM ROLE_PERMISSIONS WHERE RoleCode = 'PAYMENT_RELEASE_BOARD' AND PermissionCode = 'PAYMENT_RELEASE_BOARD')
                   AND EXISTS (SELECT 1 FROM PERMISSIONS WHERE Code = 'PAYMENT_RELEASE_BOARD')
                    INSERT INTO ROLE_PERMISSIONS (Id, RoleCode, PermissionCode) VALUES (NEWID(), 'PAYMENT_RELEASE_BOARD', 'PAYMENT_RELEASE_BOARD');
                IF NOT EXISTS (SELECT 1 FROM ROLE_MENUS WHERE RoleCode = 'PAYMENT_RELEASE_BOARD' AND MenuItemKey = 'departmentReleases')
                    INSERT INTO ROLE_MENUS (Id, RoleCode, MenuItemKey) VALUES (NEWID(), 'PAYMENT_RELEASE_BOARD', 'departmentReleases');
                IF NOT EXISTS (SELECT 1 FROM ROLE_MENUS WHERE RoleCode = 'PAYMENT_RELEASE_BOARD' AND MenuItemKey = 'purchases')
                    INSERT INTO ROLE_MENUS (Id, RoleCode, MenuItemKey) VALUES (NEWID(), 'PAYMENT_RELEASE_BOARD', 'purchases');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM ROLE_MENUS WHERE MenuItemKey = 'departmentReleases' OR RoleCode LIKE 'PAYMENT[_]RELEASE[_]%';");
            migrationBuilder.Sql("DELETE FROM ROLE_PERMISSIONS WHERE RoleCode LIKE 'PAYMENT[_]RELEASE[_]%';");
            migrationBuilder.Sql("DELETE FROM ROLES WHERE Code LIKE 'PAYMENT[_]RELEASE[_]%';");
            migrationBuilder.Sql("DELETE FROM MENU_ITEMS WHERE [Key] = 'departmentReleases';");
        }
    }
}
