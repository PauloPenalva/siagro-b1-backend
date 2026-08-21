using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.SalesContracts;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.SalesShipmentReleases;

public class SalesShipmentReleasesRecalculateShippedService(AppDbContext context)
{
    /// <summary>
    /// Tipos de romaneio que consomem o saldo da liberação de VENDA. Fonte única da regra —
    /// os hooks do faturamento consultam este predicado em vez de repetir a lista.
    /// (Devolução de venda NÃO cria romaneio novo; vira o status da <c>SalesShipment</c>
    /// original para <c>Returned</c>, que sai da soma — restaurando o saldo.)
    /// </summary>
    public static bool AffectsShippedQuantity(StorageTransactionType type) =>
        type is StorageTransactionType.SalesShipment;

    /// <summary>
    /// Calcula o volume consumido da liberação SEM persistir nada, para quem precisa
    /// decidir antes de gravar (ex.: cancelamento, que recusa quando não há saldo).
    /// Fonte: ledger SALES_CONTRACTS_ALLOCATIONS — Σ Volume assinado por liberação.
    /// Reproduz os comportamentos do faturamento sem hooks extras: devolução confirma
    /// linhas negativas na mesma liberação de origem (soma volta ao pré-faturamento),
    /// cancelamento deleta as linhas (soma zera) e realocação devolve saldo à liberação
    /// de origem (linha −) enquanto consome a do destino (linha +).
    /// </summary>
    public Task<decimal> CalculateShippedAsync(Guid salesShipmentReleaseKey) =>
        CalculateShippedAsync(context, salesShipmentReleaseKey, excludedItemKeys: null);

    /// <summary>
    /// Consumo efetivo de UMA linha, em memória — para linhas ainda não persistidas.
    /// Delega à regra do contrato: os dois eixos consomem o MESMO número (item em aberto
    /// consome o nominal; entrega encerrada consome o líquido), e a quebra apurada sai
    /// inteira da linha dona em vez de ser rateada.
    /// </summary>
    public static decimal EffectiveVolume(SalesContractAllocation allocation, SalesInvoiceItem item) =>
        SalesContractsRecalculateBalanceService.EffectiveVolume(allocation, item);

    /// <summary>
    /// Espelho, por liberação, da projeção SQL de
    /// <see cref="SalesContractsRecalculateBalanceService.CalculateAllocatedAsync(AppDbContext, Guid, ICollection{Guid}?)"/>:
    /// Σ Volume nominal − quebra apurada da linha dona. Um item <c>Open</c> deduz
    /// <c>Quantity</c>; encerrado, deduz <c>NetQuantity</c> — a mesma regra do saldo do
    /// contrato, para que os dois eixos nunca divirjam.
    ///
    /// <paramref name="excludedItemKeys"/> tira itens da soma do banco, pelo mesmo motivo do
    /// lado do contrato: nos fluxos em que a TITULARIDADE muda junto com a gravação,
    /// <c>SumAsync</c> agrega no servidor e leria a flag antiga. O chamador exclui os itens
    /// tocados e soma a contribuição deles em memória, com <see cref="EffectiveVolume"/>.
    /// </summary>
    public static async Task<decimal> CalculateShippedAsync(
        AppDbContext context, Guid salesShipmentReleaseKey, ICollection<Guid>? excludedItemKeys)
    {
        var lines = context.SalesContractsAllocations
            .Where(a => a.SalesShipmentReleaseKey == salesShipmentReleaseKey);

        if (excludedItemKeys is { Count: > 0 })
            lines = lines.Where(a => !excludedItemKeys.Contains(a.SalesInvoiceItemKey));

        var nominal = await lines.SumAsync(a => a.Volume);

        // Só a linha dona desconta, e só depois da entrega conferida.
        var shortage = await lines
            .Where(a => a.OwnsDeliveryDifference
                        && a.SalesInvoiceItem!.DeliveryStatus == SalesInvoiceDeliveryStatus.Closed)
            .SumAsync(a => a.SalesInvoiceItem!.Quantity
                           - (a.SalesInvoiceItem.DeliveredQuantity - a.SalesInvoiceItem.QuantityLoss));

        return decimal.Round(nominal - shortage, 3, MidpointRounding.ToEven);
    }

    /// <summary>
    /// Recalcula (sem SaveChanges) as liberações afetadas por uma mutação nas linhas de UM
    /// item, a partir do conjunto PÓS-MUTAÇÃO em memória — espelho de
    /// <see cref="SalesContractsRecalculateBalanceService.RecalculateForTouchedItemAsync"/>
    /// para o eixo da liberação. É o caminho da realocação e do seu estorno: as linhas novas
    /// ainda não estão no banco (e as removidas ainda estão), então o item tocado sai da
    /// soma SQL e entra por <see cref="EffectiveVolume"/>.
    ///
    /// <paramref name="extraReleaseKeys"/> cobre a liberação que perdeu TODAS as linhas do
    /// item (o estorno remove a única linha do destino): sem ela, a liberação não apareceria
    /// em <paramref name="itemLines"/> e ficaria com o saldo antigo.
    ///
    /// Diferente do gatilho por item, aqui não há congelamento por status: o ledger mudou de
    /// verdade, e cada fluxo já tem o próprio guard de status antes de chegar aqui.
    /// </summary>
    public static async Task RecalculateForTouchedItemAsync(
        AppDbContext context, Guid itemKey, SalesInvoiceItem item,
        IReadOnlyCollection<SalesContractAllocation> itemLines,
        IEnumerable<Guid>? extraReleaseKeys = null)
    {
        var excluded = new[] { itemKey };
        var releaseKeys = itemLines
            .Where(a => a.SalesShipmentReleaseKey != null)
            .Select(a => a.SalesShipmentReleaseKey!.Value)
            .Concat(extraReleaseKeys ?? [])
            .Distinct()
            .ToList();

        foreach (var releaseKey in releaseKeys)
        {
            var release = await context.SalesShipmentReleases
                .FirstOrDefaultAsync(r => r.Key == releaseKey);

            if (release is null)
                continue;

            var persisted = await CalculateShippedAsync(context, releaseKey, excluded);
            var itemSum = itemLines
                .Where(a => a.SalesShipmentReleaseKey == releaseKey)
                .Sum(a => EffectiveVolume(a, item));

            release.ShippedQuantity = decimal.Round(persisted + itemSum, 3, MidpointRounding.ToEven);
        }
    }

    /// <summary>
    /// Recalcula (em memória, sem SaveChanges — o chamador persiste) o
    /// <c>ShippedQuantity</c> de todas as liberações consumidas pelos itens informados.
    /// Espelha <see cref="SalesContractsRecalculateBalanceService.RecalculateForItemsAsync"/>:
    /// é o caminho dos hooks em que a quebra de um item muda (conferência de entrega), onde
    /// a liberação afetada pode não ser a do contrato fiscal do item — realocações espalham
    /// o item por outras liberações.
    ///
    /// Deve rodar DEPOIS do flush do recálculo do contrato: a titularidade da diferença é
    /// reeleita lá em memória, e a projeção SQL abaixo leria a flag antiga.
    ///
    /// Liberação <c>Completed</c>/<c>Cancelled</c> fica congelada, como o contrato
    /// <c>Finished</c> — quem precisar mexer usa o recálculo manual.
    /// </summary>
    public static async Task RecalculateForItemsAsync(AppDbContext context, ICollection<Guid> itemKeys)
    {
        if (itemKeys.Count == 0)
            return;

        var releaseKeys = await context.SalesContractsAllocations
            .Where(a => itemKeys.Contains(a.SalesInvoiceItemKey) && a.SalesShipmentReleaseKey != null)
            .Select(a => a.SalesShipmentReleaseKey!.Value)
            .Distinct()
            .ToListAsync();

        foreach (var releaseKey in releaseKeys)
        {
            var release = await context.SalesShipmentReleases
                .FirstOrDefaultAsync(r => r.Key == releaseKey);

            if (release is null || release.Status is ReleaseStatus.Completed or ReleaseStatus.Cancelled)
                continue;

            release.ShippedQuantity = await CalculateShippedAsync(context, releaseKey, excludedItemKeys: null);
        }
    }

    public async Task RecalculateAsync(Guid salesShipmentReleaseKey)
    {
        var release = await context.SalesShipmentReleases
            .FirstOrDefaultAsync(x => x.Key == salesShipmentReleaseKey);

        if (release is null)
            return;

        release.ShippedQuantity = await CalculateShippedAsync(salesShipmentReleaseKey);

        await context.SaveChangesAsync();
    }
}
