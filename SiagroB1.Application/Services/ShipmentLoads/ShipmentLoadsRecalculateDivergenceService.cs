using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.ShipmentLoads;

/// <summary>
/// Sexto termo do saldo da carga (GAC-1226): a diferença com sinal entre o efetivo da carga e a NF
/// do SAP vinculada, gravada em <see cref="SalesInvoiceDivergence.QuantityDifference"/>.
/// </summary>
/// <remarks>
/// Só calcula, sem gravar: o escritor único de <see cref="ShipmentLoad.InvoiceDivergenceQuantity"/> é
/// <see cref="ShipmentLoadsRecalculateInvoicedService"/>, porque o status depende deste termo.
/// <para>
/// O registro não é anulado quando o documento é devolvido: o termo é que deixa de contar. Assim,
/// cancelar a devolução faz a divergência voltar a contar sem mexer no registro. Já o cancelamento do
/// próprio documento anula o registro (<see cref="SalesInvoices.SalesInvoiceDivergenceRules"/>).
/// </para>
/// </remarks>
public static class ShipmentLoadsRecalculateDivergenceService
{
    /// <summary>Tolerância de quantidade, a mesma casa decimal das quantidades.</summary>
    private const decimal Tolerance = 0.001m;

    /// <summary>
    /// Σ QuantityDifference dos registros Open da carga cujo documento está vivo
    /// (Pending, Confirmed ou Returned) e NÃO totalmente devolvido
    /// (Σ itens − Σ itens das devoluções Confirmed com origem nele &gt; 0,001).
    /// </summary>
    /// <remarks>
    /// <paramref name="excludedInvoiceKeys"/> tem a mesma função que em
    /// <see cref="ShipmentLoadsRecalculateInvoicedService.CalculateInvoicedAsync"/>: o documento
    /// removido na mesma transação ainda está no banco, e a soma agrega NO SERVIDOR. Vale tanto para o
    /// documento da divergência quanto para a devolução.
    /// </remarks>
    public static async Task<decimal> CalculateAsync(
        AppDbContext context, Guid shipmentLoadKey, ICollection<Guid>? excludedInvoiceKeys)
    {
        var divergences = await context.SalesInvoiceDivergences
            .Where(d => d.ShipmentLoadKey == shipmentLoadKey
                        && d.Status == SalesInvoiceDivergenceStatus.Open)
            .Select(d => new { d.SalesInvoiceKey, d.QuantityDifference })
            .ToListAsync();

        if (divergences.Count == 0)
            return decimal.Zero;

        // InvoiceStatus é anulável na entidade: a lista precisa ser do MESMO tipo, senão o
        // Contains não traduz para SQL.
        var liveStatuses = new InvoiceStatus?[] { InvoiceStatus.Pending, InvoiceStatus.Confirmed, InvoiceStatus.Returned };
        var divergenceInvoiceKeys = divergences.Select(d => d.SalesInvoiceKey).Distinct().ToList();

        var liveInvoices = context.SalesInvoices
            .Where(i => divergenceInvoiceKeys.Contains(i.Key)
                        && liveStatuses.Contains(i.InvoiceStatus));

        if (excludedInvoiceKeys is { Count: > 0 })
            liveInvoices = liveInvoices.Where(i => !excludedInvoiceKeys.Contains(i.Key));

        var liveKeys = await liveInvoices.Select(i => i.Key).ToListAsync();
        if (liveKeys.Count == 0)
            return decimal.Zero;

        // SalesInvoiceItem.SalesInvoiceKey e SalesInvoiceOriginKey são Guid?: a lista de
        // comparação precisa do mesmo tipo para o Contains traduzir.
        var liveKeysNullable = liveKeys.Select(k => (Guid?)k).ToList();

        var billedByInvoice = await context.SalesInvoicesItems
            .Where(item => liveKeysNullable.Contains(item.SalesInvoiceKey))
            .GroupBy(item => item.SalesInvoiceKey)
            .Select(g => new { InvoiceKey = g.Key, Quantity = g.Sum(item => item.Quantity) })
            .ToListAsync();

        var returnInvoices = context.SalesInvoices
            .Where(i => i.InvoiceType == SalesInvoiceType.Return
                        && i.InvoiceStatus == InvoiceStatus.Confirmed
                        && i.SalesInvoiceOriginKey != null
                        && liveKeysNullable.Contains(i.SalesInvoiceOriginKey));

        if (excludedInvoiceKeys is { Count: > 0 })
            returnInvoices = returnInvoices.Where(i => !excludedInvoiceKeys.Contains(i.Key));

        var returns = await returnInvoices
            .Select(i => new { i.Key, i.SalesInvoiceOriginKey })
            .ToListAsync();
        var returnKeysNullable = returns.Select(r => (Guid?)r.Key).ToList();

        var returnedByReturn = await context.SalesInvoicesItems
            .Where(item => returnKeysNullable.Contains(item.SalesInvoiceKey))
            .GroupBy(item => item.SalesInvoiceKey)
            .Select(g => new { InvoiceKey = g.Key, Quantity = g.Sum(item => item.Quantity) })
            .ToListAsync();

        var returnedByOrigin = returns
            .Join(returnedByReturn, r => (Guid?)r.Key, q => q.InvoiceKey, (r, q) => new { r.SalesInvoiceOriginKey, q.Quantity })
            .GroupBy(x => x.SalesInvoiceOriginKey)
            .ToDictionary(g => g.Key!.Value, g => g.Sum(x => x.Quantity));

        var billedLookup = billedByInvoice.ToDictionary(x => x.InvoiceKey!.Value, x => x.Quantity);

        // Documento totalmente devolvido não chegou ao cliente: a divergência dele deixa de contar.
        var countingKeys = liveKeys
            .Where(key => billedLookup.GetValueOrDefault(key) - returnedByOrigin.GetValueOrDefault(key) > Tolerance)
            .ToHashSet();

        var total = divergences
            .Where(d => countingKeys.Contains(d.SalesInvoiceKey))
            .Sum(d => d.QuantityDifference);

        return decimal.Round(total, 3, MidpointRounding.ToEven);
    }
}
