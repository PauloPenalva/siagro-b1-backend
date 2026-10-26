using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.CommonContext
{
    /// <summary>
    /// Permissões de liberação de pagamento do contrato de compra por departamento (GAC-1188).
    /// Idempotente: o código pode já ter sido cadastrado à mão. O vínculo com ADMIN só nasce se o
    /// papel existir (FK).
    /// </summary>
    public partial class SeedPaymentReleasePermissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF NOT EXISTS (SELECT 1 FROM PERMISSIONS WHERE Code = 'PAYMENT_RELEASE_STOCK')
                    INSERT INTO PERMISSIONS (Code, Description)
                    VALUES ('PAYMENT_RELEASE_STOCK', 'Liberar o pagamento do contrato de compra pelo departamento Estoque');
                IF EXISTS (SELECT 1 FROM ROLES WHERE Code = 'ADMIN')
                   AND NOT EXISTS (SELECT 1 FROM ROLE_PERMISSIONS WHERE RoleCode = 'ADMIN' AND PermissionCode = 'PAYMENT_RELEASE_STOCK')
                    INSERT INTO ROLE_PERMISSIONS (Id, RoleCode, PermissionCode)
                    VALUES (NEWID(), 'ADMIN', 'PAYMENT_RELEASE_STOCK');
                """);

            migrationBuilder.Sql("""
                IF NOT EXISTS (SELECT 1 FROM PERMISSIONS WHERE Code = 'PAYMENT_RELEASE_LEGAL')
                    INSERT INTO PERMISSIONS (Code, Description)
                    VALUES ('PAYMENT_RELEASE_LEGAL', 'Liberar o pagamento do contrato de compra pelo departamento Jurídico');
                IF EXISTS (SELECT 1 FROM ROLES WHERE Code = 'ADMIN')
                   AND NOT EXISTS (SELECT 1 FROM ROLE_PERMISSIONS WHERE RoleCode = 'ADMIN' AND PermissionCode = 'PAYMENT_RELEASE_LEGAL')
                    INSERT INTO ROLE_PERMISSIONS (Id, RoleCode, PermissionCode)
                    VALUES (NEWID(), 'ADMIN', 'PAYMENT_RELEASE_LEGAL');
                """);

            migrationBuilder.Sql("""
                IF NOT EXISTS (SELECT 1 FROM PERMISSIONS WHERE Code = 'PAYMENT_RELEASE_FISCAL')
                    INSERT INTO PERMISSIONS (Code, Description)
                    VALUES ('PAYMENT_RELEASE_FISCAL', 'Liberar o pagamento do contrato de compra pelo departamento Fiscal');
                IF EXISTS (SELECT 1 FROM ROLES WHERE Code = 'ADMIN')
                   AND NOT EXISTS (SELECT 1 FROM ROLE_PERMISSIONS WHERE RoleCode = 'ADMIN' AND PermissionCode = 'PAYMENT_RELEASE_FISCAL')
                    INSERT INTO ROLE_PERMISSIONS (Id, RoleCode, PermissionCode)
                    VALUES (NEWID(), 'ADMIN', 'PAYMENT_RELEASE_FISCAL');
                """);

            migrationBuilder.Sql("""
                IF NOT EXISTS (SELECT 1 FROM PERMISSIONS WHERE Code = 'PAYMENT_RELEASE_ROYALTIES')
                    INSERT INTO PERMISSIONS (Code, Description)
                    VALUES ('PAYMENT_RELEASE_ROYALTIES', 'Liberar o pagamento do contrato de compra pelo departamento Royalties');
                IF EXISTS (SELECT 1 FROM ROLES WHERE Code = 'ADMIN')
                   AND NOT EXISTS (SELECT 1 FROM ROLE_PERMISSIONS WHERE RoleCode = 'ADMIN' AND PermissionCode = 'PAYMENT_RELEASE_ROYALTIES')
                    INSERT INTO ROLE_PERMISSIONS (Id, RoleCode, PermissionCode)
                    VALUES (NEWID(), 'ADMIN', 'PAYMENT_RELEASE_ROYALTIES');
                """);

            migrationBuilder.Sql("""
                IF NOT EXISTS (SELECT 1 FROM PERMISSIONS WHERE Code = 'PAYMENT_RELEASE_FINANCIAL')
                    INSERT INTO PERMISSIONS (Code, Description)
                    VALUES ('PAYMENT_RELEASE_FINANCIAL', 'Liberar o pagamento do contrato de compra pelo departamento Financeiro');
                IF EXISTS (SELECT 1 FROM ROLES WHERE Code = 'ADMIN')
                   AND NOT EXISTS (SELECT 1 FROM ROLE_PERMISSIONS WHERE RoleCode = 'ADMIN' AND PermissionCode = 'PAYMENT_RELEASE_FINANCIAL')
                    INSERT INTO ROLE_PERMISSIONS (Id, RoleCode, PermissionCode)
                    VALUES (NEWID(), 'ADMIN', 'PAYMENT_RELEASE_FINANCIAL');
                """);

            migrationBuilder.Sql("""
                IF NOT EXISTS (SELECT 1 FROM PERMISSIONS WHERE Code = 'PAYMENT_RELEASE_STORE')
                    INSERT INTO PERMISSIONS (Code, Description)
                    VALUES ('PAYMENT_RELEASE_STORE', 'Liberar o pagamento do contrato de compra pelo departamento Loja');
                IF EXISTS (SELECT 1 FROM ROLES WHERE Code = 'ADMIN')
                   AND NOT EXISTS (SELECT 1 FROM ROLE_PERMISSIONS WHERE RoleCode = 'ADMIN' AND PermissionCode = 'PAYMENT_RELEASE_STORE')
                    INSERT INTO ROLE_PERMISSIONS (Id, RoleCode, PermissionCode)
                    VALUES (NEWID(), 'ADMIN', 'PAYMENT_RELEASE_STORE');
                """);

            migrationBuilder.Sql("""
                IF NOT EXISTS (SELECT 1 FROM PERMISSIONS WHERE Code = 'PAYMENT_RELEASE_BOARD')
                    INSERT INTO PERMISSIONS (Code, Description)
                    VALUES ('PAYMENT_RELEASE_BOARD', 'Liberar o pagamento do contrato de compra pelo departamento Diretoria');
                IF EXISTS (SELECT 1 FROM ROLES WHERE Code = 'ADMIN')
                   AND NOT EXISTS (SELECT 1 FROM ROLE_PERMISSIONS WHERE RoleCode = 'ADMIN' AND PermissionCode = 'PAYMENT_RELEASE_BOARD')
                    INSERT INTO ROLE_PERMISSIONS (Id, RoleCode, PermissionCode)
                    VALUES (NEWID(), 'ADMIN', 'PAYMENT_RELEASE_BOARD');
                """);

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM ROLE_PERMISSIONS WHERE PermissionCode LIKE 'PAYMENT[_]RELEASE[_]%';");
            migrationBuilder.Sql("DELETE FROM PERMISSIONS WHERE Code LIKE 'PAYMENT[_]RELEASE[_]%';");
        }
    }
}
