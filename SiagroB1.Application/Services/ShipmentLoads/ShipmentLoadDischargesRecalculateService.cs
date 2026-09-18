using Microsoft.EntityFrameworkCore;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.ShipmentLoads;

/// <summary>
/// Escritor ÚNICO das duas quantidades derivadas do ticket de descarga (GAC-1171):
/// <c>SalesInvoiceItem.TicketDeliveredQuantity</c> e <c>ShipmentLoad.DischargedQuantity</c>.
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

            item.TicketDeliveredQuantity = await SumByItemAsync(itemKey);
        }

        var load = await context.ShipmentLoads.FirstOrDefaultAsync(x => x.Key == shipmentLoadKey);

        if (load is not null)
            load.DischargedQuantity = await SumByLoadAsync(shipmentLoadKey);
    }

    private Task<decimal> SumByItemAsync(Guid itemKey) =>
        SumAsync(
            context.ShipmentLoadsDischarges.Where(x => x.SalesInvoiceItemKey == itemKey),
            x => x.SalesInvoiceItemKey == itemKey);

    private Task<decimal> SumByLoadAsync(Guid loadKey) =>
        SumAsync(
            context.ShipmentLoadsDischarges.Where(x => x.ShipmentLoadKey == loadKey),
            x => x.ShipmentLoadKey == loadKey);

    /// <summary>
    /// Soma o que está no banco MAIS o que está no rastreador, em vez de usar <c>SumAsync</c> do
    /// EF: a agregação no servidor não vê a entidade recém-adicionada e ainda não gravada, e foi
    /// exatamente isso que já fez a diferença de entrega ser calculada com o valor anterior.
    /// </summary>
    /// <remarks>
    /// A consulta entra FILTRADA por chave (<paramref name="persistedQuery"/>): somar em memória a
    /// tabela inteira funcionaria hoje e degradaria em silêncio conforme os tickets acumulam.
    /// O predicado repete o mesmo filtro para as entidades do rastreador, que o SQL não alcança.
    /// </remarks>
    /// <remarks>
    /// ⚠️ <c>trackedKeys</c> precisa incluir as entidades <c>Deleted</c> — é o que faz a linha
    /// ainda física no banco (lida por <paramref name="persistedQuery"/>) ser descartada da soma
    /// quando o chamador removeu o ticket e recalculou ANTES do <c>SaveChanges</c> que efetiva a
    /// exclusão. Só a lista somada (<c>tracked</c>) exclui <c>Deleted</c> — a chave, não.
    /// </remarks>
    private async Task<decimal> SumAsync(
        IQueryable<Domain.Entities.ShipmentLoadDischarge> persistedQuery,
        Func<Domain.Entities.ShipmentLoadDischarge, bool> predicate)
    {
        var persisted = await persistedQuery.AsNoTracking().ToListAsync();

        var allTracked = context.ChangeTracker
            .Entries<Domain.Entities.ShipmentLoadDischarge>()
            .ToList();

        var trackedKeys = allTracked.Select(e => e.Entity.Key).ToHashSet();

        var tracked = allTracked
            .Where(e => e.State != EntityState.Deleted)
            .Select(e => e.Entity);

        return persisted
            .Where(x => !trackedKeys.Contains(x.Key))
            .Concat(tracked)
            .Where(predicate)
            .Sum(x => x.DischargedQuantity);
    }
}
