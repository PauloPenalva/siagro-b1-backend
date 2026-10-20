using System.Collections.Concurrent;
using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Nfe;

/// <summary>
/// Trava por filial da consulta de DF-e: o clique em "Consultar SEFAZ" e o job agendado (ou dois usuários) não
/// podem mandar o mesmo NSU ao mesmo tempo — a SEFAZ responde 656 e bloqueia a filial por 1 hora.
/// Devolve <c>null</c> quando outra consulta já segura a trava (sem esperar).
/// </summary>
public interface IDfeSyncLock
{
    Task<IAsyncDisposable?> TryAcquireAsync(string branchCode, CancellationToken ct = default);
}

/// <summary>Trava em memória do processo: usada nos testes (EF InMemory não tem sp_getapplock).</summary>
public sealed class InMemoryDfeSyncLock : IDfeSyncLock
{
    private readonly ConcurrentDictionary<string, byte> _held = new();

    public Task<IAsyncDisposable?> TryAcquireAsync(string branchCode, CancellationToken ct = default) =>
        Task.FromResult<IAsyncDisposable?>(_held.TryAdd(branchCode, 0) ? new Releaser(() => _held.TryRemove(branchCode, out _)) : null);

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
/// sp_getapplock com dono de SESSÃO e timeout 0 no banco da empresa: vale entre processos/servidores. A conexão
/// fica aberta até a liberação (a trava morre com a sessão se o processo cair).
/// </summary>
public sealed class SqlServerDfeSyncLock(IUnitOfWork db) : IDfeSyncLock
{
    public async Task<IAsyncDisposable?> TryAcquireAsync(string branchCode, CancellationToken ct = default)
    {
        var database = db.Context.Database;
        await database.OpenConnectionAsync(ct);

        try
        {
            var result = await ExecuteAsync(database,
                "DECLARE @r int; EXEC @r = sp_getapplock @Resource = @res, @LockMode = 'Exclusive', @LockOwner = 'Session', @LockTimeout = 0; SELECT @r;",
                Resource(branchCode), ct);

            if (result >= 0)
                return new Releaser(database, Resource(branchCode));
        }
        catch
        {
            await database.CloseConnectionAsync();
            throw;
        }

        await database.CloseConnectionAsync();
        return null;
    }

    private static string Resource(string branchCode) => $"dfe-sync:{branchCode}";

    private static async Task<int> ExecuteAsync(DatabaseFacade database, string sql, string resource, CancellationToken ct)
    {
        await using var command = database.GetDbConnection().CreateCommand();
        command.CommandText = sql;
        command.Transaction = database.CurrentTransaction?.GetDbTransaction();

        var parameter = command.CreateParameter();
        parameter.ParameterName = "@res";
        parameter.DbType = DbType.String;
        parameter.Size = 255;
        parameter.Value = resource;
        command.Parameters.Add(parameter);

        var value = await command.ExecuteScalarAsync(ct);
        return value is null or DBNull ? 0 : Convert.ToInt32(value);
    }

    private sealed class Releaser(DatabaseFacade database, string resource) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            try
            {
                await ExecuteAsync(database, "EXEC sp_releaseapplock @Resource = @res, @LockOwner = 'Session'; SELECT 0;", resource, CancellationToken.None);
            }
            finally
            {
                await database.CloseConnectionAsync();
            }
        }
    }
}
