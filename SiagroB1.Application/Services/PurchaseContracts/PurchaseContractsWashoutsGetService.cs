using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.PurchaseContracts;

/// <summary>
/// Leituras do washout. Devolve IQueryable sem Include: o [EnableQuery] compõe $expand/$select
/// sobre a consulta (PurchaseContract, PriceFixation, FinancialDocument).
/// </summary>
public class PurchaseContractsWashoutsGetService(AppDbContext context)
{
    public IQueryable<PurchaseContractWashout> QueryByContract(Guid contractKey) =>
        context.PurchaseContractsWashouts.Where(x => x.PurchaseContractKey == contractKey).AsNoTracking();

    /// <summary>Fila de aprovação: washouts em aprovação de todos os contratos.</summary>
    public IQueryable<PurchaseContractWashout> QueryPending() =>
        context.PurchaseContractsWashouts
            .Where(x => x.Status == PurchaseContractWashoutStatus.InApproval)
            .AsNoTracking();

    public IQueryable<PurchaseContractWashout> QueryByKey(Guid key) =>
        context.PurchaseContractsWashouts.Where(x => x.Key == key).AsNoTracking();
}
