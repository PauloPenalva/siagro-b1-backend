using System.Data;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Application.Services.Nfe;

/// <summary>
/// Reserva o próximo número da NF-e da filial, atômica (mesmo padrão do DocNumberSequenceService).
/// ⚠️ Roda numa conexão Dapper própria, fora da transação do EF: chame antes de abrir transação.
/// O InMemory dos testes não roda SQL cru — use o fake.
/// </summary>
public class NfeNumberReservationService(IDbConnection connection, IConfiguration configuration)
{
    /// <summary>
    /// String da conexão da trava: a da CONFIGURAÇÃO (a da conexão da requisição perde a senha
    /// depois de aberta, com Persist Security Info=False) e SEM pool — com pool, fechar devolve a
    /// conexão ociosa ao pool ainda segurando a trava de sessão, e o documento responderia
    /// "em andamento" até a conexão ser reaproveitada.
    /// </summary>
    public string EmissionLockConnectionString() =>
        new SqlConnectionStringBuilder(configuration.GetConnectionString("SiagroDB") ?? connection.ConnectionString)
        {
            Pooling = false,
        }.ConnectionString;

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

    /// <summary>
    /// Trava de aplicação (sp_getapplock) da emissão do documento: duas requisições da primeira
    /// tentativa transmitiriam duas NF-e para o mesmo documento. Conexão PRÓPRIA e fora do pool
    /// (<see cref="EmissionLockConnectionString"/>) — a da requisição pode abrir e fechar a cada
    /// comando — com dono "Session": fechar a conexão encerra a sessão e libera a trava, e uma
    /// queda do processo também. Não espera (timeout 0): quem chega depois é recusado.
    /// </summary>
    public virtual async Task<IAsyncDisposable> AcquireEmissionLockAsync(Guid invoiceKey)
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
                new { Resource = $"nfe:{invoiceKey}" });

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
