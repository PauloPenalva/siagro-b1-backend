using SiagroB1.Application.Services.Mdfe;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Application.Tests.Support;

/// <summary>Reserva de número do MDF-e em memória: conta chamadas e trava mantida.</summary>
public sealed class FakeMdfeNumberReservationService(int next = 1) : MdfeNumberReservationService(null!, null!)
{
    private int _next = next;

    public int Calls { get; private set; }

    public bool EmissionBusy { get; set; }

    public int LocksHeld { get; private set; }

    public override Task<int> ReserveAsync(string branchCode, int series)
    {
        Calls++;
        return Task.FromResult(_next++);
    }

    public override Task<IAsyncDisposable> AcquireEmissionLockAsync(Guid documentKey)
    {
        if (EmissionBusy)
            throw new DefaultException("A emissão deste documento já está em andamento. Aguarde e consulte a situação.");

        LocksHeld++;
        return Task.FromResult<IAsyncDisposable>(new Release(this));
    }

    private sealed class Release(FakeMdfeNumberReservationService owner) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            owner.LocksHeld--;
            return ValueTask.CompletedTask;
        }
    }
}
