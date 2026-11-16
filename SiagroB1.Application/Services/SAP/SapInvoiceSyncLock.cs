using System.Collections.Concurrent;
using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.SAP;

/// <summary>
/// Trava da sincronização das invoices do SAP por empresa (GAC-1226): o "Sincronizar agora" e o job agendado não
/// podem tratar os mesmos documentos ao mesmo tempo. Devolve <c>null</c> quando outra execução já segura a trava
/// (sem esperar). Mesmo desenho da trava do DF-e (<c>IDfeSyncLock</c>).
/// </summary>
public interface ISapInvoiceSyncLock
{
    Task<IAsyncDisposable?> TryAcquireAsync(string companyCode, CancellationToken ct = default);
}

/// <summary>Trava em memória do processo: usada nos testes (EF InMemory não tem sp_getapplock).</summary>
public sealed class InMemorySapInvoiceSyncLock : ISapInvoiceSyncLock
{
    private readonly ConcurrentDictionary<string, byte> _held = new(StringComparer.OrdinalIgnoreCase);

    public Task<IAsyncDisposable?> TryAcquireAsync(string companyCode, CancellationToken ct = default) =>
        Task.FromResult<IAsyncDisposable?>(_held.TryAdd(companyCode, 0)
            ? new Releaser(() => _held.TryRemove(companyCode, out _))
            : null);

    private sealed class Releaser(Action release) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            release();
            return ValueTask.CompletedTask;
        }
    }
}

/// <summary>
/// sp_getapplock com dono de SESSÃO e timeout 0 no banco da empresa (o do escopo): vale entre processos/servidores e,
/// por estar no banco da empresa, já é por empresa. A conexão fica aberta até a liberação (a trava morre com a sessão
/// se o processo cair).
/// </summary>
public sealed class SqlServerSapInvoiceSyncLock(IUnitOfWork db) : ISapInvoiceSyncLock
{
    private const string Resource = "sap-invoice-sync";

    public async Task<IAsyncDisposable?> TryAcquireAsync(string companyCode, CancellationToken ct = default)
    {
        var database = db.Context.Database;
        await database.OpenConnectionAsync(ct);

        try
        {
            var result = await ExecuteAsync(database,
                "DECLARE @r int; EXEC @r = sp_getapplock @Resource = @res, @LockMode = 'Exclusive', @LockOwner = 'Session', @LockTimeout = 0; SELECT @r;",
                ct);

            if (result >= 0)
                return new Releaser(database);
        }
        catch
        {
            await database.CloseConnectionAsync();
            throw;
        }

        await database.CloseConnectionAsync();
        return null;
    }

    private static async Task<int> ExecuteAsync(DatabaseFacade database, string sql, CancellationToken ct)
    {
        await using var command = database.GetDbConnection().CreateCommand();
        command.CommandText = sql;
        command.Transaction = database.CurrentTransaction?.GetDbTransaction();

        var parameter = command.CreateParameter();
        parameter.ParameterName = "@res";
        parameter.DbType = DbType.String;
        parameter.Size = 255;
        parameter.Value = Resource;
        command.Parameters.Add(parameter);

        var value = await command.ExecuteScalarAsync(ct);
        return value is null or DBNull ? 0 : Convert.ToInt32(value);
    }

    private sealed class Releaser(DatabaseFacade database) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            try
            {
                await ExecuteAsync(database, "EXEC sp_releaseapplock @Resource = @res, @LockOwner = 'Session'; SELECT 0;", CancellationToken.None);
            }
            finally
            {
                await database.CloseConnectionAsync();
            }
        }
    }
}
