using Microsoft.EntityFrameworkCore.Migrations;
using SiagroB1.Domain.Enums;

#nullable disable

namespace SiagroB1.Migrations.AppContext
{
    /// <summary>
    /// Classifica toda a base de lotes como <see cref="StorageOwnershipType.ThirdParty"/>.
    ///
    /// A coluna existe desde a criação da tabela, mas nenhum serviço jamais a gravou —
    /// e <c>OwnedInOurCustody</c> é o valor 0 do enum. Resultado: todo lote existente
    /// está marcado como estoque próprio, inclusive os lotes de produtor. Sem este
    /// backfill, a regra "só vincula contrato de compra quando o destino é próprio"
    /// nasceria liberando 100% dos lotes — exatamente o inverso da intenção.
    ///
    /// A classificação dos lotes que realmente são próprios passa a ser explícita, pela
    /// tela de Lotes de Armazenagem. Falha fechada: um lote não classificado não habilita
    /// o vínculo de contrato.
    /// </summary>
    /// <inheritdoc />
    public partial class BackfillStorageAddressOwnershipType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                $"UPDATE STORAGE_ADDRESSES SET OwnershipType = {(int)StorageOwnershipType.ThirdParty};");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // No-op deliberado: o valor anterior era o default do enum aplicado a todas as
            // linhas, não uma classificação real. Não há informação a restaurar.
        }
    }
}
