using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.PurchaseInvoices;

/// <summary>
/// Leitura dos comentários de um documento de entrada, mais recente primeiro. Devolve
/// <see cref="IQueryable{T}"/> para o OData ainda poder aplicar $filter/$orderby/$top.
/// </summary>
public class PurchaseInvoicesCommentsGetService(AppDbContext context)
{
    public IQueryable<PurchaseInvoiceComment> QueryAll(Guid purchaseInvoiceKey) =>
        context.PurchaseInvoicesComments
            .AsNoTracking()
            .Where(x => x.PurchaseInvoiceKey == purchaseInvoiceKey)
            .OrderByDescending(x => x.CommentedAt);
}
