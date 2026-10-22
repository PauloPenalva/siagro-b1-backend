using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Companies;

namespace SiagroB1.Application.Services.Cte;

/// <summary>
/// Reserva o próximo número do CT-e da filial, atômica (mesmo padrão do NfeNumberReservationService).
/// ⚠️ Roda numa conexão Dapper própria, fora da transação do EF: chame antes de abrir transação.
/// </summary>
public class CteNumberReservationService(IDbConnection connection, ICompanyContext companyContext)
{
    internal const string ReserveSql = """
        UPDATE BRANCH_CTE_SERIES WITH (UPDLOCK, HOLDLOCK)
        SET NextNumber = NextNumber + 1
        OUTPUT deleted.NextNumber
        WHERE BranchCode = @BranchCode AND Series = @Series;
        """;

    /// <summary>Conexão da EMPRESA da execução e SEM pool (ver NfeNumberReservationService).</summary>
    public string EmissionLockConnectionString() =>
        new SqlConnectionStringBuilder(companyContext.ConnectionString)
        {
            Pooling = false,
        }.ConnectionString;

    public virtual async Task<int> ReserveAsync(string branchCode, int series) =>
        await connection.ExecuteScalarAsync<int?>(ReserveSql, new { BranchCode = branchCode, Series = series })
        ?? throw new DefaultException($"A série {series} não está cadastrada na Configuração do CT-e da filial {branchCode}.");

    /// <summary>Trava de aplicação da emissão do documento; recurso prefixado "CTE-". Não espera: quem chega depois é recusado.</summary>
    public virtual async Task<IAsyncDisposable> AcquireEmissionLockAsync(Guid documentKey)
    {
        var lockConnection = new SqlConnection(EmissionLockConnectionString());

        try
        {
            await lockConnection.OpenAsync();

            var result = await lockConnection.ExecuteScalarAsync<int>(
                """
                DECLARE @r INT;
                EXEC @r = sp_getapplock @Resource = @Resource, @LockMode = 'Exclusive', @LockOwner = 'Session', @LockTimeout = 0;
                SELECT @r;
                """,
                new { Resource = $"CTE-{documentKey}" });

            if (result < 0)
                throw new DefaultException("A emissão deste documento já está em andamento. Aguarde e consulte a situação.");
        }
        catch
        {
            await lockConnection.DisposeAsync();
            throw;
        }

        return lockConnection;
    }
}
