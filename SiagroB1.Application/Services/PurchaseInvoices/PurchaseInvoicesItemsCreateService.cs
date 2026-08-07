using Microsoft.EntityFrameworkCore;
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

        // Linha sem contrato (caso comum: insumo, serviço, frete) não precisa do CardCode do pai —
        // pula a query e a chamada ao guard.
        if (item.PurchaseContractKey is not null)
        {
            var cardCode = await db.Context.PurchaseInvoices
                .Where(x => x.Key == item.PurchaseInvoiceKey)
                .Select(x => x.CardCode)
                .FirstAsync();

            await PurchaseInvoiceLineGuard.EnsureContractIsCompatibleAsync(
                db, item.PurchaseContractKey, item.ItemCode, cardCode);
        }

        await db.Context.PurchaseInvoicesItems.AddAsync(item);
        await db.SaveChangesAsync();
    }
}
