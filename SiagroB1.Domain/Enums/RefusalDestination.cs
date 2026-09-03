namespace SiagroB1.Domain.Enums;

/// <summary>Destino FÍSICO da mercadoria recusada/devolvida.</summary>
/// <remarks>
/// Compartilhado pelos dois fluxos de devolução — a recusa de CARGA
/// (<c>ShipmentLoadsRefuseService</c>) e o retorno de documento de saída LEGADO
/// (<c>SalesInvoicesReturnService</c>) —, porque a decisão do operador é a mesma nos dois e
/// duplicar os valores duplicaria também o parsing <c>"Rebilling"|"Warehouse"</c> das actions.
/// <para>
/// ⚠️ O que muda entre os fluxos não é o destino, é o ESTADO TERMINAL do romaneio de origem.
/// Em nenhum dos dois ele pode ficar <c>Returned</c>: aquele status significa "o embarque não
/// aconteceu" e faz o romaneio sair das consultas de saldo, re-creditando o armazém de origem —
/// o que estaria errado nos dois destinos, porque em ambos o grão saiu de lá.
/// </para>
/// </remarks>
public enum RefusalDestination
{
    /// <summary>
    /// O caminhão segue para outro destino: a mercadoria continua embarcada e volta a ficar
    /// disponível para faturamento — para o mesmo cliente ou para outro.
    /// </summary>
    Rebilling = 0,

    /// <summary>
    /// A mercadoria é descarregada num armazém, possivelmente diferente do de origem, e passa a
    /// estar disponível para novo embarque naquele armazém.
    /// </summary>
    Warehouse = 1,
}
