using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Application.Services.ShipmentLoads;

/// <summary>
/// Como se desfaz uma carga <see cref="ShipmentLoadStatus.Completed"/>, dito ao usuário nas
/// travas que a recusam (GAC-1171, melhorias).
/// </summary>
/// <remarks>
/// A mesma situação tem dois caminhos de volta. A Remoção é concluída à mão e se desfaz pelo
/// "Reabrir". A carga Normal é concluída pela Conferência de Entregas, e "Reabrir" nela seria
/// desfeito na hora pelo recálculo. Mandar a pessoa ao botão errado era o que as mensagens
/// antigas fariam na carga Normal.
/// </remarks>
public static class ShipmentLoadCompletionRules
{
    public static string UndoHint(ShipmentLoad load) =>
        load.LoadType == ShipmentLoadType.Removal
            ? "Reabra-a"
            : "Estorne a conferência de entrega";
}
