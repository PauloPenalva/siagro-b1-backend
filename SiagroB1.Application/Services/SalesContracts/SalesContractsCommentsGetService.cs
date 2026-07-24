using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.SalesContracts;

/// <summary>
/// Leitura dos comentários de um contrato de venda, mais recente primeiro. Devolve
/// <see cref="IQueryable{T}"/> para o OData ainda poder aplicar $filter/$orderby/$top.
/// </summary>
public class SalesContractsCommentsGetService(AppDbContext context)
{
    public IQueryable<SalesContractComment> QueryAll(Guid salesContractKey) =>
        context.SalesContractsComments
            .AsNoTracking()
            .Where(x => x.SalesContractKey == salesContractKey)
            .OrderByDescending(x => x.CommentedAt);
}
