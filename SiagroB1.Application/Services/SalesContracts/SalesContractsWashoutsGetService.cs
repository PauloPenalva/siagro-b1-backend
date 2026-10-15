using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.SalesContracts;

/// <summary>
/// Leituras do washout de venda. Devolve IQueryable sem Include: o [EnableQuery] compõe $expand/$select
/// (SalesContract, PriceFixation, FinancialDocument).
/// </summary>
public class SalesContractsWashoutsGetService(AppDbContext context)
{
    public IQueryable<SalesContractWashout> QueryByContract(Guid contractKey) =>
        context.SalesContractsWashouts.Where(x => x.SalesContractKey == contractKey).AsNoTracking();

    /// <summary>Fila de aprovação: washouts de venda em aprovação de todos os contratos.</summary>
    public IQueryable<SalesContractWashout> QueryPending() =>
        context.SalesContractsWashouts
            .Where(x => x.Status == SalesContractWashoutStatus.InApproval)
            .AsNoTracking();

    public IQueryable<SalesContractWashout> QueryByKey(Guid key) =>
        context.SalesContractsWashouts.Where(x => x.Key == key).AsNoTracking();
}
