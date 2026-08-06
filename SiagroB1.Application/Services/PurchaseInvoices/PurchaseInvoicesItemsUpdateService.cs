using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.PurchaseInvoices;

/// <summary>
/// Altera uma linha do documento de entrada — inclusive a AMARRAÇÃO com a nota de origem, que é o
/// que a grade edita depois de importar o XML.
///
/// A guarda de status é a do documento PAI, e é lida da linha existente e não da entrante: um
/// PATCH parcial não traz <c>PurchaseInvoiceKey</c>.
/// </summary>
public class PurchaseInvoicesItemsUpdateService(IUnitOfWork db, IItemService itemService)
{
    public async Task ExecuteAsync(Guid key, PurchaseInvoiceItem entity, string userName)
    {
        var existing = await db.Context.PurchaseInvoicesItems
                           .FirstOrDefaultAsync(x => x.Key == key)
                       ?? throw new NotFoundException("Linha do documento de entrada não encontrada.");

        await PurchaseInvoiceLineGuard.EnsureParentIsPendingAsync(db, existing.PurchaseInvoiceKey);

        existing.ItemCode = entity.ItemCode;
        existing.ItemName = await PurchaseInvoiceLineGuard.ResolveItemNameAsync(
            itemService, entity.ItemCode, entity.ItemName);
        existing.Quantity = entity.Quantity;
        existing.UnitPrice = entity.UnitPrice;
        existing.UnitOfMeasureCode = entity.UnitOfMeasureCode;
        existing.SalesInvoiceItemKey = entity.SalesInvoiceItemKey;
        existing.PurchaseInvoiceItemOriginKey = entity.PurchaseInvoiceItemOriginKey;

        await db.SaveChangesAsync();
    }
}
