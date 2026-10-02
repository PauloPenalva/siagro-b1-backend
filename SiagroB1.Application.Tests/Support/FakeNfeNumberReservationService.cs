using SiagroB1.Application.Services.Nfe;

namespace SiagroB1.Application.Tests.Support;

/// <summary>Numeração em memória (o InMemory não roda o UPDATE ... OUTPUT).</summary>
public sealed class FakeNfeNumberReservationService(int next = 1) : NfeNumberReservationService(null!)
{
    private int _next = next;

    public int Calls { get; private set; }

    public override Task<int> ReserveAsync(string branchCode)
    {
        Calls++;
        return Task.FromResult(_next++);
    }
}
