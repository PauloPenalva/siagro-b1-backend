using SiagroB1.Infra;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Tests.Support;

/// <summary>
/// Conta as chamadas de transação de um serviço. O InMemory não desfaz o que já foi salvo, então a
/// atomicidade de um fluxo composto é provada pela ORDEM: quem é dono da transação abre e fecha;
/// quem roda em <c>CommitMode.Deferred</c> não toca nela.
/// </summary>
public sealed class CountingUnitOfWork(UnitOfWork inner) : IUnitOfWork
{
    public int Begins { get; private set; }
    public int Commits { get; private set; }
    public int Rollbacks { get; private set; }

    public AppDbContext Context => inner.Context;

    public Task BeginTransactionAsync()
    {
        Begins++;
        return inner.BeginTransactionAsync();
    }

    public Task CommitAsync()
    {
        Commits++;
        return inner.CommitAsync();
    }

    public Task RollbackAsync()
    {
        Rollbacks++;
        return inner.RollbackAsync();
    }

    public Task SaveChangesAsync() => inner.SaveChangesAsync();
}
