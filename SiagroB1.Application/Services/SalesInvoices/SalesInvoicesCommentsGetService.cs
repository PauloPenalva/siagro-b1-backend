using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.SalesInvoices;

/// <summary>
/// Leitura dos comentários de um documento de saída, mais recente primeiro. Devolve
/// <see cref="IQueryable{T}"/> para o OData ainda poder aplicar $filter/$orderby/$top.
/// </summary>
public class SalesInvoicesCommentsGetService(AppDbContext context)
{
    public IQueryable<SalesInvoiceComment> QueryAll(Guid salesInvoiceKey) =>
        context.SalesInvoicesComments
            .AsNoTracking()
            .Where(x => x.SalesInvoiceKey == salesInvoiceKey)
            .OrderByDescending(x => x.CommentedAt);
}
