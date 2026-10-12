using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <inheritdoc />
    public partial class MigrateWarehousesToBusinessPartners : Migration
    {
        /// <summary>
        /// O armazém passa a ser o parceiro de negócio com QryGroup23 = 'Y' (como no SAPB1). Cada linha
        /// de WAREHOUSES vira PN com o MESMO código, então nenhum documento é regravado (lotes,
        /// movimentos, contratos, liberações e cargas guardam o código). PN já existente com o código
        /// só ganha a marca. Idempotente; em base SAPB1 WAREHOUSES é vazia e nada acontece.
        /// </summary>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE bp SET QryGroup23 = 'Y'
                FROM BUSINESS_PARTNERS bp
                INNER JOIN WAREHOUSES w ON w.Code = bp.CardCode
                WHERE ISNULL(bp.QryGroup23, '') <> 'Y';

                INSERT INTO BUSINESS_PARTNERS (CardCode, CardName, CardType, QryGroup23, U_YKT_CNPJ_CPF)
                SELECT w.Code, w.Name, 'S', 'Y', NULLIF(LTRIM(RTRIM(w.TAXID)), '')
                FROM WAREHOUSES w
                WHERE NOT EXISTS (SELECT 1 FROM BUSINESS_PARTNERS bp WHERE bp.CardCode = w.Code);
                """);
        }

        /// <summary>Sem volta: desmarcar/apagar PN poderia atingir parceiros que o usuário já completou.</summary>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
