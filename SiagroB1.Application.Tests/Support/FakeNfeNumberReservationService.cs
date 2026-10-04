using SiagroB1.Application.Services.Nfe;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Application.Tests.Support;

/// <summary>Numeração em memória (o InMemory não roda o UPDATE ... OUTPUT nem o sp_getapplock).</summary>
public sealed class FakeNfeNumberReservationService(int next = 1) : NfeNumberReservationService(null!, null!)
{
    private int _next = next;

    public int Calls { get; private set; }

    /// <summary>Simula a trava de emissão já tomada por outra requisição.</summary>
    public bool EmissionBusy { get; set; }

    public int LockAttempts { get; private set; }

    public int LocksHeld { get; private set; }

    public override Task<int> ReserveAsync(string branchCode)
    {
        Calls++;
        return Task.FromResult(_next++);
    }

    public override Task<IAsyncDisposable> AcquireEmissionLockAsync(Guid invoiceKey)
    {
        LockAttempts++;

        if (EmissionBusy)
            throw new DefaultException("A emissão deste documento já está em andamento. Aguarde e consulte a situação.");

        LocksHeld++;
        return Task.FromResult<IAsyncDisposable>(new Release(this));
    }

    private sealed class Release(FakeNfeNumberReservationService owner) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            owner.LocksHeld--;
            return ValueTask.CompletedTask;
        }
    }
}
