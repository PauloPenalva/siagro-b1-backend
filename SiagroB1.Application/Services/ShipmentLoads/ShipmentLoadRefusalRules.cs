using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Application.Services.ShipmentLoads;

/// <summary>
/// Trava da carga com recusa aguardando NF-e de entrada (spec 2026-10-09 §6). Uma regra e uma mensagem para todas
/// as operações que mexem em saldo, ticket ou transbordo — quem lê a mensagem precisa saber os dois caminhos.
/// </summary>
public static class ShipmentLoadRefusalRules
{
    public static string PendingMessage(string? loadCode) =>
        $"A carga {loadCode} tem uma recusa aguardando NF-e de entrada: emita as NF-e ou cancele a recusa.";

    public static void EnsureNoPendingRefusal(ShipmentLoad load)
    {
        if (load.Status == ShipmentLoadStatus.RefusalPending)
            throw new DefaultException(PendingMessage(load.Code));
    }
}
