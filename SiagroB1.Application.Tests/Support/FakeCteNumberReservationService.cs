using SiagroB1.Application.Services.Cte;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Application.Tests.Support;

/// <summary>Numeração do CT-e em memória (o InMemory não roda o UPDATE ... OUTPUT nem o sp_getapplock).</summary>
public sealed class FakeCteNumberReservationService(int next = 1) : CteNumberReservationService(null!, null!)
{
    private int _next = next;

    public int Calls { get; private set; }

    public bool EmissionBusy { get; set; }

    public int LocksHeld { get; private set; }

    public List<int> ReservedSeries { get; } = [];

    public override Task<int> ReserveAsync(string branchCode, int series)
    {
        Calls++;
        ReservedSeries.Add(series);
        return Task.FromResult(_next++);
    }

    public override Task<IAsyncDisposable> AcquireEmissionLockAsync(Guid documentKey)
    {
        if (EmissionBusy)
            throw new DefaultException("A emissão deste documento já está em andamento. Aguarde e consulte a situação.");

        LocksHeld++;
        return Task.FromResult<IAsyncDisposable>(new Release(this));
    }

    private sealed class Release(FakeCteNumberReservationService owner) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            owner.LocksHeld--;
            return ValueTask.CompletedTask;
        }
    }
}
