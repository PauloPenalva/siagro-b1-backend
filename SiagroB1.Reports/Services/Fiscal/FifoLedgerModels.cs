namespace SiagroB1.Reports.Services.Fiscal;

/// <summary>Natureza do movimento de estoque fiscal para o custeio PEPS.</summary>
public enum InventoryMovementKind
{
    Opening,
    Entry,
    Exit,
    PurchaseReturn,
    SalesReturn,
    ValueComplement,
}

/// <summary>
/// Movimento normalizado que alimenta o motor PEPS. <paramref name="Value"/> é o custo nas
/// entradas/complementos e 0 nas saídas. <paramref name="Source"/> é opaco para o motor (o
/// chamador anexa nota/parceiro/CFOP para exibição).
/// </summary>
public record InventoryMovement(
    Guid LineKey,
    DateTime Date,
    int DaySequence,
    InventoryMovementKind Kind,
    decimal Quantity,
    decimal Value,
    Guid? OriginLineKey,
    string? UnitOfMeasure,
    object? Source);

/// <summary>
/// Linha valorizada do Kardex, com saldo acumulado após o movimento. <c>PendingValue</c>: complemento
/// sem camada aberta — não entra no saldo agora; soma no ValueIn da próxima entrada que criar camada.
/// <c>CostNotFound</c>: devolução de venda sem saída de origem conhecida e sem camada aberta — entrou a custo zero.
/// </summary>
public record ValuedMovement(
    InventoryMovement Movement,
    decimal QuantityIn,
    decimal QuantityOut,
    decimal UnitCost,
    decimal ValueIn,
    decimal ValueOut,
    decimal BalanceQuantity,
    decimal BalanceValue,
    bool InsufficientBalance,
    bool IsNegativeBalanceAdjustment,
    bool PendingValue = false,
    bool CostNotFound = false);

/// <summary>Camada PEPS ainda aberta (quantidade sempre &gt; 0).</summary>
public record FifoLayer(Guid LineKey, DateTime Date, decimal Quantity, decimal Value);

/// <summary>Resultado do custeio: linhas valorizadas e camadas que sobraram (base do inventário).</summary>
public record FifoResult(List<ValuedMovement> Lines, List<FifoLayer> OpenLayers);
