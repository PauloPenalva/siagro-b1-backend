using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.PurchaseInvoices;

/// <summary>
/// Inclui uma linha no documento de entrada.
///
/// A guarda de status é aplicada aqui e não só no Update do cabeçalho — ver
/// <see cref="PurchaseInvoiceLineGuard"/>.
/// </summary>
public class PurchaseInvoicesItemsCreateService(IUnitOfWork db, IItemService itemService)
{
    public async Task ExecuteAsync(PurchaseInvoiceItem item, string userName)
    {
        await PurchaseInvoiceLineGuard.EnsureParentIsPendingAsync(db, item.PurchaseInvoiceKey);

        item.ItemName = await PurchaseInvoiceLineGuard.ResolveItemNameAsync(
            itemService, item.ItemCode, item.ItemName);

        await db.Context.PurchaseInvoicesItems.AddAsync(item);
        await db.SaveChangesAsync();
    }
}
