using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.CommonContext
{
    /// <inheritdoc />
    public partial class SeedSalesContractFiscalEditPermission : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Idempotente: o código pode já ter sido cadastrado à mão. O vínculo com ADMIN só nasce se o papel existir (FK).
            migrationBuilder.Sql("""
                IF NOT EXISTS (SELECT 1 FROM PERMISSIONS WHERE Code = 'SALES_CONTRACT_FISCAL_EDIT')
                    INSERT INTO PERMISSIONS (Code, Description)
                    VALUES ('SALES_CONTRACT_FISCAL_EDIT', 'Editar o complemento fiscal do contrato de venda');
                IF EXISTS (SELECT 1 FROM ROLES WHERE Code = 'ADMIN')
                   AND NOT EXISTS (SELECT 1 FROM ROLE_PERMISSIONS WHERE RoleCode = 'ADMIN' AND PermissionCode = 'SALES_CONTRACT_FISCAL_EDIT')
                    INSERT INTO ROLE_PERMISSIONS (Id, RoleCode, PermissionCode)
                    VALUES (NEWID(), 'ADMIN', 'SALES_CONTRACT_FISCAL_EDIT');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DELETE FROM ROLE_PERMISSIONS WHERE PermissionCode = 'SALES_CONTRACT_FISCAL_EDIT';");
            migrationBuilder.Sql("DELETE FROM PERMISSIONS WHERE Code = 'SALES_CONTRACT_FISCAL_EDIT';");
        }
    }
}
