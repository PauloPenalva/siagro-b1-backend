using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.PurchaseContracts;

/// <summary>
/// Leitura dos comentários de um contrato de compra, mais recente primeiro. Devolve
/// <see cref="IQueryable{T}"/> para o OData ainda poder aplicar $filter/$orderby/$top.
/// </summary>
public class PurchaseContractsCommentsGetService(AppDbContext context)
{
    public IQueryable<PurchaseContractComment> QueryAll(Guid purchaseContractKey) =>
        context.PurchaseContractsComments
            .AsNoTracking()
            .Where(x => x.PurchaseContractKey == purchaseContractKey)
            .OrderByDescending(x => x.CommentedAt);
}
