using SiagroB1.Domain.Entities;

namespace SiagroB1.Application.Services.Cte;

/// <summary>
/// Gancho chamado uma única vez, logo depois que o CT-e é gravado como Autorizado (na transmissão ou na recuperação pela
/// consulta). A entrega B gera aqui o título a receber.
/// </summary>
public interface ICteAuthorizationHook
{
    Task OnAuthorizedAsync(TransportDocument doc);
}
