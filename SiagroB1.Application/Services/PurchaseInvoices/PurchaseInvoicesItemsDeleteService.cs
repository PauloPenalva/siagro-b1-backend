using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.PurchaseInvoices;

/// <summary>
/// Exclui uma linha do documento de entrada. Só com o documento pai PENDENTE — ver
/// <see cref="PurchaseInvoiceLineGuard"/>.
/// </summary>
public class PurchaseInvoicesItemsDeleteService(IUnitOfWork db)
{
    public async Task ExecuteAsync(Guid key)
    {
        var item = await db.Context.PurchaseInvoicesItems
                       .FirstOrDefaultAsync(x => x.Key == key)
                   ?? throw new NotFoundException("Linha do documento de entrada não encontrada.");

        await PurchaseInvoiceLineGuard.EnsureParentIsPendingAsync(db, item.PurchaseInvoiceKey);

        var nfeStatus = await db.Context.PurchaseInvoices
            .Where(x => x.Key == item.PurchaseInvoiceKey)
            .Select(x => x.NfeStatus)
            .FirstAsync();
        PurchaseInvoiceNfeLock.EnsureLinesChangeable(nfeStatus);

        db.Context.PurchaseInvoicesItems.Remove(item);
        await db.SaveChangesAsync();
    }
}
