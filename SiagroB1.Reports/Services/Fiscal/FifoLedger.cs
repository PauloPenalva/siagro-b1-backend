namespace SiagroB1.Reports.Services.Fiscal;

/// <summary>
/// Motor PEPS (FIFO) puro do estoque fiscal — sem EF, sem entidades. Regras (spec itens 4–6):
/// saída consome as camadas mais antigas; sem saldo, o restante sai a custo zero e a linha é
/// marcada; a entrada seguinte cobre o déficit numa linha de "acerto de saldo negativo" pelo custo
/// dela; devolução de compra baixa da camada de origem; devolução de venda entra pelo custo com que
/// a saída de origem foi baixada; complemento de valor soma na camada de origem (ou mais antiga, ou
/// na próxima a ser criada). Custo unitário com 6 casas, valores com 2 (ToEven); o último consumo de
/// uma camada leva o valor que restou nela, sem centavo residual.
/// </summary>
public static class FifoLedger
{
    private sealed class Layer(Guid lineKey, DateTime date, decimal quantity, decimal value)
    {
        public Guid LineKey { get; } = lineKey;
        public DateTime Date { get; } = date;
        public decimal Quantity { get; set; } = quantity;
        public decimal Value { get; set; } = value;

        /// <summary>Custo unitário fixado na criação; só muda com complemento de valor.</summary>
        public decimal UnitCost { get; set; } = Cost(value, quantity);
    }

    public static FifoResult Run(IEnumerable<InventoryMovement> movements)
    {
        var layers = new List<Layer>();
        var lines = new List<ValuedMovement>();
        var exitCosts = new Dictionary<Guid, (decimal Qty, decimal Value)>();
        decimal deficit = 0m;        // quantidade que saiu sem camada (saldo negativo)
        decimal pendingValue = 0m;   // complemento sem camada aberta, aguardando a próxima

        decimal BalanceQty() => layers.Sum(l => l.Quantity) - deficit;
        decimal BalanceValue() => layers.Sum(l => l.Value) + pendingValue;

        void Add(InventoryMovement m, decimal qIn, decimal qOut, decimal vIn, decimal vOut,
            bool insufficient = false, bool adjustment = false)
        {
            var uc = qIn > 0 ? Cost(vIn, qIn) : qOut > 0 ? Cost(vOut, qOut) : 0m;
            lines.Add(new ValuedMovement(m, qIn, qOut, uc, vIn, vOut, BalanceQty(), BalanceValue(),
                insufficient, adjustment));
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

        // Saída FIFO; devolve (valor, quantidade que ficou sem camada).
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

        // Entrada: cobre o déficit (linha de acerto) e cria camada com o restante.
        void Inflow(InventoryMovement m, decimal qty, decimal value)
        {
            var cover = Math.Min(deficit, qty);
            var adjustValue = cover == 0 ? 0m : cover == qty ? value : Money(cover * Cost(value, qty));
            var rest = qty - cover;
            if (rest > 0)
            {
                layers.Add(new Layer(m.LineKey, m.Date, rest, value - adjustValue + pendingValue));
                pendingValue = 0m;
            }
            deficit -= cover;
            if (cover > 0)
            {
                // Saldo intermediário: entrada inteira antes do acerto.
                lines.Add(new ValuedMovement(m, qty, 0m, Cost(value, qty), value, 0m,
                    BalanceQty() + cover, BalanceValue() + adjustValue, false, false));
                Add(m, 0m, cover, 0m, adjustValue, adjustment: true);
            }
            else
            {
                Add(m, qty, 0m, value, 0m);
            }
        }

        foreach (var m in Order(movements))
        {
            switch (m.Kind)
            {
                case InventoryMovementKind.Opening:
                case InventoryMovementKind.Entry:
                    Inflow(m, m.Quantity, m.Value);
                    break;

                case InventoryMovementKind.SalesReturn:
                {
                    var uc = m.OriginLineKey is { } o && exitCosts.TryGetValue(o, out var e) && e.Qty > 0
                        ? Cost(e.Value, e.Qty) : 0m;
                    Inflow(m, m.Quantity, Money(m.Quantity * uc));
                    break;
                }

                case InventoryMovementKind.ValueComplement:
                {
                    var target = layers.FirstOrDefault(l => l.LineKey == m.OriginLineKey) ?? layers.FirstOrDefault();
                    if (target is null)
                        pendingValue += m.Value;
                    else
                    {
                        target.Value += m.Value;
                        target.UnitCost = Cost(target.Value, target.Quantity);
                    }
                    Add(m, 0m, 0m, m.Value, 0m);
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
                    value += fifoValue;
                    deficit += missing;
                    if (m.Kind == InventoryMovementKind.Exit)
                    {
                        exitCosts.TryGetValue(m.LineKey, out var prev);
                        exitCosts[m.LineKey] = (prev.Qty + m.Quantity, prev.Value + value);
                    }
                    Add(m, 0m, m.Quantity, 0m, value, insufficient: missing > 0);
                    break;
                }
            }
        }

        return new FifoResult(lines,
            layers.Select(l => new FifoLayer(l.LineKey, l.Date, l.Quantity, l.Value)).ToList());
    }

    /// <summary>Data; no mesmo dia, saldo inicial, depois entradas, depois saídas; então DaySequence.</summary>
    private static IEnumerable<InventoryMovement> Order(IEnumerable<InventoryMovement> movements) =>
        movements
            .OrderBy(m => m.Date)
            .ThenBy(m => m.Kind switch
            {
                InventoryMovementKind.Opening => 0,
                InventoryMovementKind.Entry or InventoryMovementKind.SalesReturn
                    or InventoryMovementKind.ValueComplement => 1,
                _ => 2,
            })
            .ThenBy(m => m.DaySequence);

    private static decimal Cost(decimal value, decimal qty) =>
        qty == 0 ? 0m : Math.Round(value / qty, 6, MidpointRounding.ToEven);

    private static decimal Money(decimal v) => Math.Round(v, 2, MidpointRounding.ToEven);
}
