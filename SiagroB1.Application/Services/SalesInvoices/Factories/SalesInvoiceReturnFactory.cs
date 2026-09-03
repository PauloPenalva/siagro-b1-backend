using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Application.Services.SalesInvoices.Factories;

/// <summary>
/// Monta a nota de DEVOLUÇÃO a partir da nota de origem — total ou parcial.
/// </summary>
/// <remarks>
/// Existe para haver um lugar só que sabe montar uma devolução. Antes a montagem morava inteira
/// dentro de <see cref="SalesInvoicesReturnService"/>, e a recusa de carga
/// (<c>ShipmentLoadsRefuseService</c>) precisa da MESMA montagem com quantidade parcial — sem
/// arrastar junto o resto daquele serviço, que abre transação própria e fecha a origem.
/// <para>
/// <b>A natureza de operação NÃO é copiada, de propósito.</b> A linha da devolução chega ao
/// <c>SalesInvoicesUsageGuardService</c> sem <c>UsageCode</c> e recebe a natureza PADRÃO — é o
/// comportamento documentado lá ("o retorno de documento de saída nasce da cópia do original,
/// cujas linhas chegam aqui sem natureza"). Copiar a natureza da origem mudaria o CFOP de toda
/// devolução já emitida.
/// </para>
/// <para>
/// <see cref="SalesInvoiceCopyFactory"/> copia <c>ShipmentLoadKey</c>, e isso é essencial: é o
/// que liga origem e retorno à mesma carga na fórmula de
/// <c>ShipmentLoadsRecalculateInvoicedService.CalculateInvoicedAsync</c>.
/// </para>
/// </remarks>
public static class SalesInvoiceReturnFactory
{
    /// <summary>
    /// Cria a devolução da <paramref name="origin"/>.
    /// </summary>
    /// <param name="quantitiesByOriginItemKey">
    /// Quantidade a devolver por chave do item de ORIGEM. <c>null</c> devolve tudo, na
    /// quantidade cheia de cada item — o caminho total, idêntico ao que existia antes.
    /// Item ausente do dicionário, ou com quantidade menor ou igual a zero, fica de fora da
    /// devolução.
    /// </param>
    public static SalesInvoice CreateFrom(
        SalesInvoice origin,
        string userName,
        IReadOnlyDictionary<Guid, decimal>? quantitiesByOriginItemKey = null)
    {
        ArgumentNullException.ThrowIfNull(origin);

        var returnInvoice = SalesInvoiceCopyFactory.CreateFrom(origin, userName);

        returnInvoice.InvoiceType = SalesInvoiceType.Return;
        returnInvoice.InvoiceStatus = InvoiceStatus.Pending;
        returnInvoice.SalesInvoiceOriginKey = origin.Key;
        returnInvoice.Items.Clear();

        foreach (var item in origin.Items)
        {
            var quantity = ResolveQuantity(item, quantitiesByOriginItemKey);

            if (quantity <= decimal.Zero)
                continue;

            returnInvoice.AddItem(new SalesInvoiceItem
            {
                ItemCode = item.ItemCode,
                ItemName = item.ItemName,
                Quantity = quantity,
                UnitPrice = item.UnitPrice,
                UnitOfMeasureCode = item.UnitOfMeasureCode,
                SalesInvoiceItemOriginKey = item.Key,
                SalesContractKey = item.SalesContractKey,
            });
        }

        if (returnInvoice.Items.Count == 0)
            throw new ApplicationException("Informe a quantidade a devolver de ao menos um item.");

        // Peso do CABEÇALHO derivado das linhas: SalesInvoiceCopyFactory copia os pesos CHEIOS
        // da origem, e numa devolução parcial isso registraria uma carreta inteira voltando
        // quando voltou um terço. Vale para o caminho total também — ali a soma das quantidades
        // é o mesmo número, e derivar sempre é o que impede o cabeçalho de virar um valor
        // independente da linha. Ver SalesInvoicesReturnWeightService.
        SalesInvoicesReturnWeightService.Apply(returnInvoice);

        return returnInvoice;
    }

    private static decimal ResolveQuantity(
        SalesInvoiceItem item,
        IReadOnlyDictionary<Guid, decimal>? quantitiesByOriginItemKey)
    {
        if (quantitiesByOriginItemKey == null)
            return item.Quantity;

        // Item sem chave só existe antes de gravar; uma origem já persistida sempre a tem.
        if (item.Key is not { } key)
            return decimal.Zero;

        return quantitiesByOriginItemKey.TryGetValue(key, out var quantity) ? quantity : decimal.Zero;
    }
}
