namespace SiagroB1.Domain.Enums;

/// <summary>
/// O que cada <see cref="ReleaseOrigin"/> decide. Fonte única das três perguntas que o
/// sistema faz sobre a origem de uma liberação de embarque.
/// </summary>
/// <remarks>
/// São <b>três eixos independentes</b>, e é de propósito que não coincidam: duas origens
/// concordam em um eixo e divergem em outro. Espalhar as comparações
/// (<c>== OwnershipTransfer</c>, <c>!= Standard</c>) pelos serviços foi o que quase fez o
/// embarque de devolução estourar "liberação de transferência sem lote" — o predicado certo
/// era o terceiro, não o primeiro. Acrescentar uma quarta origem obriga a responder aqui às
/// três perguntas, num lugar só.
/// </remarks>
public static class ReleaseOriginRules
{
    /// <summary>
    /// O físico JÁ está em nosso estoque quando a liberação nasce, então a Expedição de Grãos
    /// cria <b>só a perna de saída</b> — sem <see cref="StorageTransactionType.Purchase"/> e sem
    /// alocar contrato — e é o <see cref="StorageTransactionType.SalesShipment"/> que consome a
    /// liberação. Criar a perna de compra aqui creditaria o armazém uma segunda vez.
    /// </summary>
    public static bool ShipsWithoutPurchaseLeg(ReleaseOrigin origin) =>
        origin != ReleaseOrigin.Standard;

    /// <summary>
    /// A liberação desconta volume do contrato de compra. Falso apenas em
    /// <see cref="ReleaseOrigin.SalesReturn"/>: aquele volume já foi debitado do contrato quando
    /// a mercadoria saiu pela primeira vez, e contá-lo de novo duplicaria o liberado.
    /// </summary>
    public static bool ConsumesPurchaseContract(ReleaseOrigin origin) =>
        origin != ReleaseOrigin.SalesReturn;

    /// <summary>
    /// A liberação nasce de um lote de armazenagem próprio e o embarque precisa drená-lo — sem
    /// isso a entrada gravada na origem vira saldo fantasma permanente. Só a transferência de
    /// titularidade: a devolução ao armazém é entrada em nível de ARMAZÉM e nasce sem lote.
    /// </summary>
    public static bool RequiresStorageAddress(ReleaseOrigin origin) =>
        origin == ReleaseOrigin.OwnershipTransfer;
}
