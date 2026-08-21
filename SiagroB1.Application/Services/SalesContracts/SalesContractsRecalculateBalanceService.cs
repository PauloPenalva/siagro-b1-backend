using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.SalesContracts;

public class SalesContractsRecalculateBalanceService(AppDbContext context)
{
    /// <summary>
    /// Σ Volume assinado do ledger − quebra dos itens cuja linha DONA está no contrato,
    /// derivado-da-soma (nunca incremental) — fonte única usada por todos os hooks e pelo
    /// backfill.
    ///
    /// A quebra apurada no fechamento da entrega NÃO é rateada entre os contratos que
    /// dividem o item: ela sai inteira do contrato da linha marcada com
    /// <see cref="SalesContractAllocation.OwnsDeliveryDifference"/>, e as demais linhas
    /// consomem o volume nominal (ver SalesContractsDeliveryDifferenceOwnerService).
    /// Item ainda aberto não tem quebra apurada e conta o nominal.
    ///
    /// A LIBERAÇÃO de entrega segue a MESMA regra, sobre as mesmas linhas, apenas agrupada
    /// por liberação em vez de contrato — ver SalesShipmentReleasesRecalculateShippedService.
    /// Os dois eixos consomem o mesmo número, então a quebra devolve saldo aos dois.
    ///
    /// O total consumido não muda com a concentração: Σ Volume − quebra = NetQuantity, que
    /// é o mesmo que o fator pró-rata anterior produzia somando todos os contratos.
    /// </summary>
    /// <summary>
    /// Consumo efetivo de UMA linha, em memória — mesma regra da projeção SQL de
    /// <see cref="CalculateAllocatedAsync"/>, para linhas ainda não persistidas.
    /// <see cref="SalesInvoiceItem.AssessedShortage"/> já devolve 0 com a entrega aberta.
    /// </summary>
    public static decimal EffectiveVolume(SalesContractAllocation allocation, SalesInvoiceItem item) =>
        allocation.Volume - (allocation.OwnsDeliveryDifference ? item.AssessedShortage : 0m);

    public static Task<decimal> CalculateAllocatedAsync(AppDbContext context, Guid salesContractKey) =>
        CalculateAllocatedAsync(context, salesContractKey, excludedItemKeys: null);

    /// <summary>
    /// <paramref name="excludedItemKeys"/> tira itens da soma do banco. Serve aos fluxos em
    /// que a TITULARIDADE de um item muda junto com a gravação (as duas pontas da realocação,
    /// o fechamento de entrega, a devolução): como <c>SumAsync</c> agrega no servidor, ele
    /// leria a flag antiga enquanto a nova ainda está apenas rastreada sob
    /// CommitMode.Deferred. O chamador exclui os itens tocados daqui e soma a contribuição
    /// deles em memória, com <see cref="EffectiveVolume"/>.
    /// </summary>
    public static async Task<decimal> CalculateAllocatedAsync(
        AppDbContext context, Guid salesContractKey, ICollection<Guid>? excludedItemKeys)
    {
        var lines = context.SalesContractsAllocations
            .Where(a => a.SalesContractKey == salesContractKey);

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
    /// Recalcula (sem SaveChanges) os contratos afetados por uma mutação nas linhas de UM
    /// item, a partir do conjunto PÓS-MUTAÇÃO em memória. É o caminho dos fluxos em que a
    /// titularidade da diferença muda junto com a gravação: a flag nova ainda não está no
    /// banco, então o item tocado sai da soma SQL e entra por <see cref="EffectiveVolume"/>.
    ///
    /// Recalcula TODO contrato que aparece em <paramref name="itemLines"/>, não só os dois
    /// lados da realocação: a titularidade pode recair sobre a linha de um terceiro contrato,
    /// vindo de uma realocação anterior do mesmo item. Contratos encerrados são ignorados.
    /// </summary>
    public static async Task RecalculateForTouchedItemAsync(
        AppDbContext context, Guid itemKey, SalesInvoiceItem item,
        IReadOnlyCollection<SalesContractAllocation> itemLines,
        IEnumerable<Guid>? extraContractKeys = null)
    {
        var excluded = new[] { itemKey };
        var contractKeys = itemLines
            .Select(a => a.SalesContractKey)
            .Concat(extraContractKeys ?? [])
            .Distinct()
            .ToList();

        foreach (var contractKey in contractKeys)
        {
            var contract = await context.SalesContracts
                .FirstOrDefaultAsync(c => c.Key == contractKey);

            if (contract is null || contract.Status == ContractStatus.Finished)
                continue;

            var persisted = await CalculateAllocatedAsync(context, contractKey, excluded);
            var itemSum = itemLines
                .Where(a => a.SalesContractKey == contractKey)
                .Sum(a => EffectiveVolume(a, item));

            contract.AllocatedVolume = decimal.Round(persisted + itemSum, 3, MidpointRounding.ToEven);
        }
    }

    /// <summary>
    /// Recalcula (em memória, sem SaveChanges — o chamador persiste) o AllocatedVolume de
    /// todos os contratos com alocação nos itens informados. Usado pelos hooks em que a
    /// quebra de um item muda (fechamento de entrega, conferência): o contrato fiscal do
    /// item pode não ser o único afetado — realocações espalham o item por outros contratos.
    /// Contratos encerrados são ignorados (excluídos do recálculo, como no lado de compra).
    /// </summary>
    public static async Task RecalculateForItemsAsync(AppDbContext context, ICollection<Guid> itemKeys)
    {
        if (itemKeys.Count == 0)
            return;

        // Reelege o dono ANTES de somar: um item sem dono (linha legada, ou dona apagada por
        // estorno) faria a quebra sumir do recálculo. A devolução pode zerar o líquido do
        // contrato dono e mover a titularidade — por isso a reeleição roda aqui, não só nos
        // fluxos de realocação.
        var touched = await SalesContractsDeliveryDifferenceOwnerService
            .EnsureOwnerAsync(context, itemKeys);

        var contractKeys = touched.Select(a => a.SalesContractKey).Distinct().ToList();

        foreach (var contractKey in contractKeys)
        {
            var contract = await context.SalesContracts
                .FirstOrDefaultAsync(c => c.Key == contractKey);

            if (contract is null || contract.Status == ContractStatus.Finished)
                continue;

            // Os itens tocados saem da soma SQL e entram em memória: a titularidade recém
            // reeleita ainda não foi persistida, e SumAsync agrega no servidor.
            var persisted = await CalculateAllocatedAsync(context, contractKey, itemKeys);
            var inMemory = touched
                .Where(a => a.SalesContractKey == contractKey)
                .Sum(a => EffectiveVolume(a, a.SalesInvoiceItem!));

            contract.AllocatedVolume = decimal.Round(persisted + inMemory, 3, MidpointRounding.ToEven);
        }
    }

    public async Task<SalesContractRecalcResultDto> ExecuteAsync(Guid key)
    {
        var contract = await context.SalesContracts
                           .FirstOrDefaultAsync(x => x.Key == key)
                       ?? throw new NotFoundException("Contrato não encontrado.");

        if (contract.Status == ContractStatus.Finished)
            throw new ApplicationException("Contrato encerrado não participa do recálculo de saldo.");

        var result = await RecalculateAsync(contract);
        await context.SaveChangesAsync();
        return result;
    }

    public async Task<SalesContractRecalcAllResultDto> ExecuteAllAsync()
    {
        var contracts = await context.SalesContracts
            .Where(x => x.Status != ContractStatus.Finished)
            .ToListAsync();

        var changes = new List<SalesContractRecalcResultDto>();
        foreach (var contract in contracts)
        {
            var result = await RecalculateAsync(contract);
            if (result.Changed)
                changes.Add(result);
        }

        await context.SaveChangesAsync();

        return new SalesContractRecalcAllResultDto
        {
            Scanned = contracts.Count,
            Changed = changes.Count,
            Changes = changes,
        };
    }

    private async Task<SalesContractRecalcResultDto> RecalculateAsync(SalesContract contract)
    {
        var newAllocated = await CalculateAllocatedAsync(context, contract.Key);

        var previousAllocated = contract.AllocatedVolume;
        var previousAvaiable = contract.AvaiableVolume;

        contract.AllocatedVolume = newAllocated;

        return new SalesContractRecalcResultDto
        {
            Key = contract.Key,
            Code = contract.Code,
            PreviousAllocatedVolume = previousAllocated,
            NewAllocatedVolume = newAllocated,
            PreviousAvaiableVolume = previousAvaiable,
            NewAvaiableVolume = contract.AvaiableVolume,
            Changed = previousAllocated != newAllocated,
        };
    }
}
