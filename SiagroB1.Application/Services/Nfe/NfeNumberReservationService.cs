using System.Data;
using Dapper;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Application.Services.Nfe;

/// <summary>
/// Reserva o próximo número da NF-e da filial, atômica (mesmo padrão do DocNumberSequenceService).
/// ⚠️ Roda numa conexão Dapper própria, fora da transação do EF: chame antes de abrir transação.
/// O InMemory dos testes não roda SQL cru — use o fake.
/// </summary>
public class NfeNumberReservationService(IDbConnection connection)
{
    public virtual async Task<int> ReserveAsync(string branchCode)
    {
        const string sql = """
            UPDATE BRANCH_NFE_SETTINGS WITH (UPDLOCK, HOLDLOCK)
            SET NextNumber = NextNumber + 1
            OUTPUT deleted.NextNumber
            WHERE BranchCode = @BranchCode;
            """;

        return await connection.ExecuteScalarAsync<int?>(sql, new { BranchCode = branchCode })
               ?? throw new DefaultException($"A filial {branchCode} não tem a Configuração da NF-e.");
    }
}
