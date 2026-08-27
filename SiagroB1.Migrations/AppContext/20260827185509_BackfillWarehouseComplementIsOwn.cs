using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <summary>
    /// O gate "destino é próprio" da transferência de titularidade deixou de olhar
    /// STORAGE_ADDRESSES.OwnershipType (nível lote) e passou a olhar
    /// WAREHOUSE_COMPLEMENTS.IsOwn (nível armazém). Como a tabela nasceu vazia, sem este
    /// backfill toda transferência com contrato passaria a ser recusada até alguém
    /// cadastrar armazém por armazém.
    /// <para>
    /// Critério: é armazém próprio todo armazém que hoje abriga algum lote classificado
    /// como estoque próprio (OwnershipType = 0, OwnedInOurCustody) — exatamente o
    /// conjunto que o gate antigo aprovava. Ninguém ganha permissão nova.
    /// </para>
    /// Só ACRESCENTA: não desmarca IsOwn de quem já foi cadastrado à mão pela tela.
    /// </summary>
    public partial class BackfillWarehouseComplementIsOwn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Idempotente nas duas metades: o INSERT filtra por NOT EXISTS e o UPDATE é
            // uma reescrita do mesmo valor. Reexecutar a migration não duplica chave.
            migrationBuilder.Sql(@"
                INSERT INTO WAREHOUSE_COMPLEMENTS (WarehouseCode, IsParticipant, IsOwn)
                SELECT DISTINCT SA.WarehouseCode, 0, 1
                FROM STORAGE_ADDRESSES SA
                WHERE SA.OwnershipType = 0
                  AND SA.WarehouseCode IS NOT NULL
                  AND NOT EXISTS (
                      SELECT 1 FROM WAREHOUSE_COMPLEMENTS WC
                      WHERE WC.WarehouseCode = SA.WarehouseCode);
            ");

            migrationBuilder.Sql(@"
                UPDATE WC
                SET WC.IsOwn = 1
                FROM WAREHOUSE_COMPLEMENTS WC
                WHERE WC.IsOwn = 0
                  AND EXISTS (
                      SELECT 1 FROM STORAGE_ADDRESSES SA
                      WHERE SA.WarehouseCode = WC.WarehouseCode
                        AND SA.OwnershipType = 0);
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Sem reversão: o backfill não é distinguível de uma marcação feita pela tela,
            // e desmarcar tudo destruiria cadastro legítimo.
        }
    }
}
