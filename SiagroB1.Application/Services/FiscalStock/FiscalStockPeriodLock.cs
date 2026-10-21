using System.Collections.Concurrent;
using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.FiscalStock;

/// <summary>
/// Trava por filial do fechamento/reabertura do mês do estoque fiscal: dois fechamentos (ou reabrir M−1 enquanto
/// fecha M) não podem conferir e gravar ao mesmo tempo — dobrariam o instantâneo ou quebrariam a ordem dos meses.
/// Chamada DENTRO da transação; espera a outra operação terminar (até o timeout).
/// </summary>
public interface IFiscalStockPeriodLock
{
    Task<IAsyncDisposable> AcquireAsync(string branchCode, CancellationToken ct = default);
}

public static class FiscalStockPeriodLockMessages
{
    public const string Busy = "Outro fechamento ou reabertura do estoque fiscal desta filial está em andamento. Tente de novo.";
}

/// <summary>Trava em memória do processo: usada nos testes (EF InMemory não tem sp_getapplock).</summary>
public sealed class InMemoryFiscalStockPeriodLock : IFiscalStockPeriodLock
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Locks = new();

    public async Task<IAsyncDisposable> AcquireAsync(string branchCode, CancellationToken ct = default)
    {
        var semaphore = Locks.GetOrAdd(branchCode, _ => new SemaphoreSlim(1, 1));
        if (!await semaphore.WaitAsync(TimeSpan.FromSeconds(30), ct))
            throw new DefaultException(FiscalStockPeriodLockMessages.Busy);
        return new Releaser(semaphore);
    }

    private sealed class Releaser(SemaphoreSlim semaphore) : IAsyncDisposable
    {
        private int _released;

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0) semaphore.Release();
            return ValueTask.CompletedTask;
        }
    }
}

/// <summary>
/// sp_getapplock com dono TRANSAÇÃO (morre no commit/rollback) e espera de até 30 s, no banco da empresa:
/// vale entre processos/servidores. Exige transação aberta no <see cref="IUnitOfWork"/>.
/// </summary>
public sealed class SqlServerFiscalStockPeriodLock(IUnitOfWork db) : IFiscalStockPeriodLock
{
    public async Task<IAsyncDisposable> AcquireAsync(string branchCode, CancellationToken ct = default)
    {
        var database = db.Context.Database;
        var transaction = database.CurrentTransaction
                          ?? throw new InvalidOperationException("A trava do período exige transação aberta.");

        await using var command = database.GetDbConnection().CreateCommand();
        command.CommandText =
            "DECLARE @r int; EXEC @r = sp_getapplock @Resource = @res, @LockMode = 'Exclusive', @LockOwner = 'Transaction', @LockTimeout = 30000; SELECT @r;";
        command.Transaction = transaction.GetDbTransaction();
        var parameter = command.CreateParameter();
        parameter.ParameterName = "@res";
        parameter.DbType = DbType.String;
        parameter.Size = 255;
        parameter.Value = $"fiscal-stock-period:{branchCode}";
        command.Parameters.Add(parameter);

        var value = await command.ExecuteScalarAsync(ct);
        var result = value is null or DBNull ? -999 : Convert.ToInt32(value);
        if (result < 0)
            throw new DefaultException(FiscalStockPeriodLockMessages.Busy);

        return NoopReleaser.Instance;
    }

    private sealed class NoopReleaser : IAsyncDisposable
    {
        public static readonly NoopReleaser Instance = new();
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
