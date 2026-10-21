namespace SiagroB1.Infra.FiscalStock;

/// <summary>
/// Motor PEPS (FIFO) puro do estoque fiscal — sem EF, sem entidades. Regras: saída consome as camadas
/// mais antigas; sem saldo, o restante fica pendente e a linha é marcada; a entrada seguinte cobre a
/// pendência pelo custo dela, somando no valor da própria saída (nunca há linha automática de acerto —
/// o saldo pode ficar negativo em quantidade e valor no meio do período); devolução de compra baixa da
/// camada de origem; devolução de venda entra pelo custo com que a saída de origem foi baixada até ali
/// (origem desconhecida: camada mais antiga em aberto); complemento de valor soma na camada de origem
/// (ou mais antiga, ou na próxima a ser criada); ajuste de saída sai pelo valor informado. Custo unitário
/// com 6 casas, valores com 2 (ToEven); o último consumo de uma camada leva o valor que restou nela.
/// </summary>
public static class FifoLedger
{
    private sealed class Layer(Guid lineKey, DateTime date, decimal quantity, decimal value)
    {
        public Guid LineKey { get; } = lineKey;
        public DateTime Date { get; } = date;
        public decimal Quantity { get; set; } = quantity;
        public decimal Value { get; set; } = value;

        /// <summary>Custo unitário fixado na criação; só muda com complemento de valor ou ajuste.</summary>
        public decimal UnitCost { get; set; } = Cost(value, quantity);
    }

    /// <param name="seed">Camadas iniciais (instantâneo), em ordem PEPS.</param>
    public static FifoResult Run(IEnumerable<InventoryMovement> movements, IEnumerable<FifoLayer>? seed = null)
    {
        var layers = (seed ?? []).Select(l => new Layer(l.LineKey, l.Date, l.Quantity, l.Value)).ToList();
        var seedQty = layers.Sum(l => l.Quantity);
        var seedValue = layers.Sum(l => l.Value);
        var lines = new List<ValuedMovement>();
        var exitCosts = new Dictionary<Guid, (decimal Qty, decimal Value)>();
        // saídas sem camada, da mais antiga à mais nova; FixedValue = ajuste de saída, cujo valor já foi informado
        var pending = new List<(int Index, decimal Qty, bool FixedValue)>();
        decimal pendingValue = 0m; // complemento sem camada aberta, fora do saldo até a próxima camada
        decimal residual = 0m;     // valor que está no saldo mas não tem camada (diferença do ajuste de saída)

        void Add(InventoryMovement m, decimal qIn, decimal qOut, decimal vIn, decimal vOut,
            bool insufficient = false, bool pendingComplement = false)
        {
            var uc = qIn > 0 ? Cost(vIn, qIn) : qOut > 0 ? Cost(vOut, qOut) : 0m;
            lines.Add(new ValuedMovement(m, qIn, qOut, uc, vIn, vOut, 0m, 0m, insufficient, [], pendingComplement));
        }

        void TrackExitCost(InventoryMovement m, decimal qty, decimal value)
        {
            if (m.Kind != InventoryMovementKind.Exit) return;
            exitCosts.TryGetValue(m.LineKey, out var prev);
            exitCosts[m.LineKey] = (prev.Qty + qty, prev.Value + value);
        }

        // Retira qty de uma camada; o último consumo leva o valor restante.
        decimal Take(Layer layer, decimal qty)
        {
            decimal value;
            if (qty >= layer.Quantity)
            {
                value = layer.Value;
                layers.Remove(layer);
            }
            else
            {
                value = Money(qty * layer.UnitCost);
                layer.Quantity -= qty;
                layer.Value -= value;
            }
            return value;
        }

        (decimal Value, decimal Missing) ConsumeFifo(decimal qty)
        {
            decimal value = 0m;
            while (qty > 0 && layers.Count > 0)
            {
                var layer = layers[0];
                var take = Math.Min(qty, layer.Quantity);
                value += Take(layer, take);
                qty -= take;
            }
            return (value, qty);
        }

        // Entrada: cobre as saídas pendentes (mais antiga primeiro) pelo custo dela, somando no ValueOut da
        // própria saída; o excedente vira camada com o valor restante (levando o complemento pendente).
        void Inflow(InventoryMovement m, decimal qty, decimal value)
        {
            var unit = Cost(value, qty);
            var restQty = qty;
            var restValue = value;
            while (restQty > 0 && pending.Count > 0)
            {
                var (index, pq, fixedValue) = pending[0];
                var cover = Math.Min(pq, restQty);
                var coverValue = cover == restQty ? restValue : Money(cover * unit);
                restQty -= cover;
                restValue -= coverValue;
                var line = lines[index];
                var stillPending = pq - cover;
                if (fixedValue)
                {
                    // ajuste de saída já saiu pelo valor informado: o custo da cobertura fica no saldo, sem camada
                    residual += coverValue;
                    lines[index] = line with
                    {
                        CoveredByLineKeys = [.. line.CoveredByLineKeys, m.LineKey],
                        InsufficientBalance = stillPending > 0,
                    };
                }
                else
                {
                    lines[index] = line with
                    {
                        ValueOut = line.ValueOut + coverValue,
                        UnitCost = Cost(line.ValueOut + coverValue, line.QuantityOut),
                        CoveredByLineKeys = [.. line.CoveredByLineKeys, m.LineKey],
                        InsufficientBalance = stillPending > 0,
                    };
                    TrackExitCost(line.Movement, 0m, coverValue);
                }
                if (stillPending > 0) pending[0] = (index, stillPending, fixedValue);
                else pending.RemoveAt(0);
            }

            var valueIn = value;
            if (restQty > 0)
            {
                // a camada nova absorve o complemento pendente (que entra no ValueIn) e o resíduo (que já
                // está no saldo, fora do ValueIn), sem ficar negativa; o que não couber segue no resíduo
                valueIn += pendingValue;
                var layerValue = restValue + pendingValue + residual;
                residual = Math.Min(layerValue, 0m);
                layers.Add(new Layer(m.LineKey, m.Date, restQty, layerValue - residual));
                pendingValue = 0m;
            }
            Add(m, qty, 0m, valueIn, 0m);
        }

        void Outflow(InventoryMovement m, decimal value, decimal missing, bool fixedValue = false)
        {
            TrackExitCost(m, m.Quantity, value);
            if (missing > 0) pending.Add((lines.Count, missing, fixedValue));
            Add(m, 0m, m.Quantity, 0m, value, insufficient: missing > 0);
        }

        foreach (var m in Order(movements))
        {
            switch (m.Kind)
            {
                case InventoryMovementKind.Opening:
                case InventoryMovementKind.Entry:
                case InventoryMovementKind.AdjustmentIn:
                    Inflow(m, m.Quantity, m.Value);
                    break;

                case InventoryMovementKind.SalesReturn:
                {
                    // Saída de origem conhecida: custo com que foi baixada até aqui (sem re-encadear coberturas
                    // futuras). Senão: custo da camada mais antiga em aberto; sem camada, zero e marcado.
                    decimal uc;
                    var notFound = false;
                    if (m.OriginLineKey is { } o && exitCosts.TryGetValue(o, out var e) && e.Qty > 0)
                        uc = Cost(e.Value, e.Qty);
                    else if (layers.Count > 0)
                        uc = layers[0].UnitCost;
                    else
                    {
                        uc = 0m;
                        notFound = true;
                    }
                    var at = lines.Count;
                    Inflow(m, m.Quantity, Money(m.Quantity * uc));
                    if (notFound)
                        lines[at] = lines[at] with { CostNotFound = true };
                    break;
                }

                case InventoryMovementKind.ValueComplement:
                {
                    var target = layers.FirstOrDefault(l => l.LineKey == m.OriginLineKey) ?? layers.FirstOrDefault();
                    if (target is null)
                    {
                        pendingValue += m.Value;
                        Add(m, 0m, 0m, 0m, 0m, pendingComplement: true);
                    }
                    else
                    {
                        target.Value += m.Value;
                        target.UnitCost = Cost(target.Value, target.Quantity);
                        Add(m, 0m, 0m, m.Value, 0m);
                    }
                    break;
                }

                case InventoryMovementKind.AdjustmentOut:
                {
                    // A quantidade disponível sai das camadas pelo FIFO ao valor contábil. A diferença
                    // D = contábil baixado − informado reavalia as camadas que sobraram, em ordem, sem deixar
                    // nenhuma negativa; o que não couber vai para o resíduo (no saldo, sem camada). A linha sai
                    // pelo valor informado; a falta de quantidade vira pendência de valor fixo.
                    var (book, missing) = ConsumeFifo(m.Quantity);
                    var d = book - m.Value;
                    foreach (var layer in layers)
                    {
                        if (d == 0) break;
                        var applied = Math.Max(d, -layer.Value);
                        layer.Value += applied;
                        layer.UnitCost = Cost(layer.Value, layer.Quantity);
                        d -= applied;
                    }
                    residual += d;
                    Outflow(m, m.Value, missing, fixedValue: true);
                    break;
                }

                case InventoryMovementKind.Exit:
                case InventoryMovementKind.PurchaseReturn:
                {
                    var qty = m.Quantity;
                    decimal value = 0m;
                    if (m.Kind == InventoryMovementKind.PurchaseReturn
                        && layers.FirstOrDefault(l => l.LineKey == m.OriginLineKey) is { } origin)
                    {
                        var take = Math.Min(qty, origin.Quantity);
                        value += Take(origin, take);
                        qty -= take;
                    }
                    var (fifoValue, missing) = ConsumeFifo(qty);
                    Outflow(m, value + fifoValue, missing);
                    break;
                }
            }
        }

        // Saldos acumulados linha a linha, já com as saídas recusteadas pelas entradas que as cobriram.
        decimal bq = seedQty, bv = seedValue;
        for (var i = 0; i < lines.Count; i++)
        {
            var l = lines[i];
            bq += l.QuantityIn - l.QuantityOut;
            bv += l.ValueIn - l.ValueOut;
            lines[i] = l with { BalanceQuantity = bq, BalanceValue = bv };
        }

        return new FifoResult(lines,
            layers.Select(l => new FifoLayer(l.LineKey, l.Date, l.Quantity, l.Value)).ToList(),
            pending.Sum(p => p.Qty), pendingValue, residual);
    }

    /// <summary>Data; no mesmo dia, saldo inicial, depois entradas, depois saídas; então DaySequence.</summary>
    private static IEnumerable<InventoryMovement> Order(IEnumerable<InventoryMovement> movements) =>
        movements
            .OrderBy(m => m.Date)
            .ThenBy(m => m.Kind switch
            {
                InventoryMovementKind.Opening => 0,
                InventoryMovementKind.Entry or InventoryMovementKind.SalesReturn
                    or InventoryMovementKind.ValueComplement or InventoryMovementKind.AdjustmentIn => 1,
                _ => 2,
            })
            .ThenBy(m => m.DaySequence);

    private static decimal Cost(decimal value, decimal qty) =>
        qty == 0 ? 0m : Math.Round(value / qty, 6, MidpointRounding.ToEven);

    private static decimal Money(decimal v) => Math.Round(v, 2, MidpointRounding.ToEven);
}
