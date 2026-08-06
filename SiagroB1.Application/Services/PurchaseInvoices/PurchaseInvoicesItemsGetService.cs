using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.PurchaseInvoices;

/// <summary>
/// Leitura das linhas do documento de entrada. Devolve <see cref="IQueryable{T}"/> para o OData
/// aplicar $filter/$expand/$orderby — é o que a grade da tela consome.
/// </summary>
public class PurchaseInvoicesItemsGetService(IUnitOfWork db)
{
    public IQueryable<PurchaseInvoiceItem> QueryAll() =>
        db.Context.PurchaseInvoicesItems.AsNoTracking();

    /// <summary>
    /// Linhas de UM documento, servindo a navegação <c>PurchaseInvoices({key})/Items</c>.
    ///
    /// A grade de itens lê por essa navegação sempre que o contexto do cabeçalho deixa de ser
    /// transiente — é o que acontece assim que o POST do documento retorna. Sem esta rota o
    /// usuário leva um 404 na cara logo depois de gravar, com o documento já persistido.
    /// </summary>
    public IQueryable<PurchaseInvoiceItem> QueryAll(Guid purchaseInvoiceKey) =>
        db.Context.PurchaseInvoicesItems
            .AsNoTracking()
            .Where(x => x.PurchaseInvoiceKey == purchaseInvoiceKey);

    public async Task<PurchaseInvoiceItem?> GetByIdAsync(Guid key) =>
        await db.Context.PurchaseInvoicesItems
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Key == key);
}
