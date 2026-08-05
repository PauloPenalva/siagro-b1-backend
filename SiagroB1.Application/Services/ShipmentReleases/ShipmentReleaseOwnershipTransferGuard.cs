using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Application.Services.ShipmentReleases;

/// <summary>
/// Guarda inversa da invariante "uma transferência de titularidade, uma liberação".
/// A transferência manda no par: mexer na liberação pela tela de Liberações
/// devolveria saldo ao contrato deixando a transferência encerrada e o grão parado
/// no lote próprio.
/// </summary>
/// <remarks>
/// Delete e Approvation não precisam da guarda: a liberação de transferência nasce
/// <c>Actived</c>, e os dois já recusam qualquer coisa que não seja <c>Pending</c>.
/// </remarks>
internal static class ShipmentReleaseOwnershipTransferGuard
{
    public const string Message =
        "Liberação originada de transferência de propriedade: cancele a transferência.";

    public static void Ensure(ShipmentRelease release)
    {
        if (release.Origin == ReleaseOrigin.OwnershipTransfer)
            throw new ApplicationException(Message);
    }
}
