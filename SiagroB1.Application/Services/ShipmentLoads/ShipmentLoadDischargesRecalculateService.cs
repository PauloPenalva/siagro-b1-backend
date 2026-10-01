using Microsoft.EntityFrameworkCore;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.ShipmentLoads;

/// <summary>
/// Escritor ÚNICO das duas quantidades derivadas do ticket de descarga (GAC-1171):
/// <c>SalesInvoiceItem.TicketDeliveredQuantity</c> (soma das parcelas do rateio que apontam para a
/// linha) e <c>ShipmentLoad.DischargedQuantity</c> (soma do peso do papel de cada ticket).
///
/// Apenas enfileira as escritas no contexto — quem chama decide quando salvar, para que a soma e
/// o ticket que a causou entrem no mesmo <c>SaveChanges</c>.
/// </summary>
/// <remarks>
/// ⚠️ Este serviço NÃO escreve em <c>DeliveredQuantity</c>, <c>QuantityLoss</c> ou
/// <c>DeliveryStatus</c>, e NÃO chama <c>SalesContractsRecalculateBalanceService</c> nem
/// <c>SalesShipmentReleasesRecalculateShippedService</c>. Essa ausência é a regra de negócio, não
/// um esquecimento: a conferência de entrega é mandatória e soberana, e o peso do ticket — que
/// pode ter sido adulterado pelo transportador — não pode virar o número que move saldo. Quem move
/// saldo continua sendo um único ato: o encerramento da conferência.
/// </remarks>
public class ShipmentLoadDischargesRecalculateService(AppDbContext context)
{
    public async Task RecalculateAsync(Guid shipmentLoadKey, IEnumerable<Guid> salesInvoiceItemKeys)
    {
        foreach (var itemKey in salesInvoiceItemKeys.Distinct())
        {
            var item = await context.SalesInvoicesItems
                .FirstOrDefaultAsync(x => x.Key == itemKey);

            // Chave desconhecida não é erro: a exclusão em lote pode citar item já removido.
            if (item is null) continue;

            item.TicketDeliveredQuantity = await SumSharesAsync(itemKey);
        }

        var load = await context.ShipmentLoads.FirstOrDefaultAsync(x => x.Key == shipmentLoadKey);

        if (load is not null)
            load.DischargedQuantity = await SumTicketsAsync(shipmentLoadKey);
    }

    /// <summary>Linha da nota: soma das PARCELAS do rateio que apontam para ela.</summary>
    private Task<decimal> SumSharesAsync(Guid itemKey) =>
        SumAsync(
            context.ShipmentLoadsDischargesItems.Where(x => x.SalesInvoiceItemKey == itemKey),
            x => x.SalesInvoiceItemKey == itemKey,
            x => x.Key,
            x => x.Quantity);

    /// <summary>Carga: soma do peso do PAPEL de cada ticket.</summary>
    private Task<decimal> SumTicketsAsync(Guid loadKey) =>
        SumAsync(
            context.ShipmentLoadsDischarges.Where(x => x.ShipmentLoadKey == loadKey),
            x => x.ShipmentLoadKey == loadKey,
            x => x.Key,
            x => x.DischargedQuantity);

    /// <summary>
    /// Soma o que está no banco MAIS o que está no rastreador, em vez de usar <c>SumAsync</c> do
    /// EF: a agregação no servidor não vê a entidade recém-adicionada e ainda não gravada, e foi
    /// exatamente isso que já fez a diferença de entrega ser calculada com o valor anterior.
    /// </summary>
    /// <remarks>
    /// A consulta entra FILTRADA por chave (<paramref name="persistedQuery"/>): somar em memória a
    /// tabela inteira funcionaria hoje e degradaria em silêncio conforme os tickets acumulam.
    /// O predicado repete o mesmo filtro para as entidades do rastreador, que o SQL não alcança.
    /// <para>
    /// ⚠️ <c>trackedKeys</c> precisa incluir as entidades <c>Deleted</c> — é o que faz a linha
    /// ainda física no banco (lida por <paramref name="persistedQuery"/>) ser descartada da soma
    /// quando o chamador a removeu e recalculou ANTES do <c>SaveChanges</c> que efetiva a
    /// exclusão. Só a lista somada (<c>tracked</c>) exclui <c>Deleted</c> — a chave, não.
    /// </para>
    /// </remarks>
    private async Task<decimal> SumAsync<T>(
        IQueryable<T> persistedQuery,
        Func<T, bool> predicate,
        Func<T, Guid?> keyOf,
        Func<T, decimal> selector) where T : class
    {
        var persisted = await persistedQuery.AsNoTracking().ToListAsync();

        var allTracked = context.ChangeTracker.Entries<T>().ToList();

        var trackedKeys = allTracked.Select(e => keyOf(e.Entity)).ToHashSet();

        var tracked = allTracked
            .Where(e => e.State != EntityState.Deleted)
            .Select(e => e.Entity);

        return persisted
            .Where(x => !trackedKeys.Contains(keyOf(x)))
            .Concat(tracked)
            .Where(predicate)
            .Sum(selector);
    }
}
