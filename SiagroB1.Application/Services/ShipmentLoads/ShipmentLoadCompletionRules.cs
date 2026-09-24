using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Application.Services.ShipmentLoads;

/// <summary>
/// O que fazer com uma carga <see cref="ShipmentLoadStatus.Completed"/>, dito ao usuário nas
/// travas que a recusam (GAC-1171, melhorias). São duas dicas: desfazer a conclusão
/// (<see cref="UndoHint"/>) e destravar a composição (<see cref="CompositionHint"/>).
/// </summary>
/// <remarks>
/// A mesma situação tem dois caminhos de volta. A Remoção é concluída à mão e se desfaz pelo
/// "Reabrir". A carga Normal é concluída pela Conferência de Entregas, e "Reabrir" nela seria
/// desfeito na hora pelo recálculo. Mandar a pessoa ao botão errado era o que as mensagens
/// antigas fariam na carga Normal.
/// </remarks>
public static class ShipmentLoadCompletionRules
{
    /// <summary>
    /// Como DESFAZER a conclusão, isto é, voltar a carga à situação anterior. Usada pela Recusa.
    /// </summary>
    public static string UndoHint(ShipmentLoad load) =>
        load.LoadType == ShipmentLoadType.Removal
            ? "Reabra-a"
            : "Estorne a conferência de entrega";

    /// <summary>
    /// Como destravar a COMPOSIÇÃO da carga concluída: usada por Vincular, Desvincular e Cancelar.
    /// </summary>
    /// <remarks>
    /// Difere de <see cref="UndoHint"/> na carga Normal, e de propósito. Estornar a conferência só
    /// desfaz a Concluída: a carga volta a Faturada (ou Descarregada), e a trava de composição
    /// continua recusando porque as notas ainda consomem a carga ("Cancele ou devolva o
    /// documento antes"). O que destrava a composição é tirar o faturamento, e cancelar os
    /// documentos de saída faz as duas coisas de uma vez: a carga deixa a Concluída e a
    /// composição fica livre. Na Remoção não há nota, e "Reabrir" resolve os dois casos.
    /// </remarks>
    public static string CompositionHint(ShipmentLoad load) =>
        load.LoadType == ShipmentLoadType.Removal
            ? "Reabra-a"
            : "Cancele os documentos de saída";
}
