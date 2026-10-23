using SiagroB1.Domain.Entities;

namespace SiagroB1.Application.Services.Cte;

/// <summary>
/// Gancho chamado depois que o CT-e é cancelado na SEFAZ e gravado como Cancelado. A implementação real
/// (<see cref="CteLifecycleHooks"/>) cancela o título e a averbação.
/// </summary>
public interface ICteCancellationHook
{
    Task OnCancelledAsync(TransportDocument doc);
}
