using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.CommonContext
{
    /// <inheritdoc />
    public partial class RemovePurchaseContractFiscalEditPermission : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Idempotente: DELETE de linha inexistente não falha (a permissão pode nunca ter sido semeada aqui).
            migrationBuilder.Sql("DELETE FROM ROLE_PERMISSIONS WHERE PermissionCode = 'PURCHASE_CONTRACT_FISCAL_EDIT';");
            migrationBuilder.Sql("DELETE FROM PERMISSIONS WHERE Code = 'PURCHASE_CONTRACT_FISCAL_EDIT';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF NOT EXISTS (SELECT 1 FROM PERMISSIONS WHERE Code = 'PURCHASE_CONTRACT_FISCAL_EDIT')
                    INSERT INTO PERMISSIONS (Code, Description)
                    VALUES ('PURCHASE_CONTRACT_FISCAL_EDIT', 'Editar o complemento fiscal do contrato de compra');
                IF EXISTS (SELECT 1 FROM ROLES WHERE Code = 'ADMIN')
                   AND NOT EXISTS (SELECT 1 FROM ROLE_PERMISSIONS WHERE RoleCode = 'ADMIN' AND PermissionCode = 'PURCHASE_CONTRACT_FISCAL_EDIT')
                    INSERT INTO ROLE_PERMISSIONS (Id, RoleCode, PermissionCode)
                    VALUES (NEWID(), 'ADMIN', 'PURCHASE_CONTRACT_FISCAL_EDIT');
                """);
        }
    }
}
