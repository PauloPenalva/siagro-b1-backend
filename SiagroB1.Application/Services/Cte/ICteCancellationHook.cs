using SiagroB1.Domain.Entities;

namespace SiagroB1.Application.Services.Cte;

/// <summary>
/// Gancho chamado depois que o CT-e é cancelado na SEFAZ e gravado como Cancelado. A entrega B pluga aqui o
/// cancelamento do título e da averbação; por ora nada acontece.
/// </summary>
public interface ICteCancellationHook
{
    Task OnCancelledAsync(TransportDocument doc);
}

/// <summary>Implementação vazia: o cancelamento do CT-e não tem efeitos colaterais até a entrega B.</summary>
public class NoOpCteCancellationHook : ICteCancellationHook
{
    public Task OnCancelledAsync(TransportDocument doc) => Task.CompletedTask;
}
