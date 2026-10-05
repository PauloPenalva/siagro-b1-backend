using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Taxes;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.PurchaseInvoices;

/// <summary>
/// Exclui uma linha do documento de entrada. Só com o documento pai PENDENTE — ver
/// <see cref="PurchaseInvoiceLineGuard"/>.
/// </summary>
public class PurchaseInvoicesItemsDeleteService(IUnitOfWork db, TaxCalculationGate gate)
{
    public async Task ExecuteAsync(Guid key)
    {
        var item = await db.Context.PurchaseInvoicesItems
                       .FirstOrDefaultAsync(x => x.Key == key)
                   ?? throw new NotFoundException("Linha do documento de entrada não encontrada.");

        await PurchaseInvoiceLineGuard.EnsureParentIsPendingAsync(db, item.PurchaseInvoiceKey);

        var invoice = await db.Context.PurchaseInvoices.FirstAsync(x => x.Key == item.PurchaseInvoiceKey);
        PurchaseInvoiceNfeLock.EnsureLinesChangeable(invoice.NfeStatus);

        // Valor declarado do terceiro Normal: a soma das linhas que ficam, como na emissão própria.
        if (PurchaseInvoiceDeclaredTotal.AppliesTo(invoice) && await gate.IsActiveAsync(invoice.BranchCode))
            await PurchaseInvoiceDeclaredTotal.ApplyAsync(db, invoice, item.Key, null);

        db.Context.PurchaseInvoicesItems.Remove(item);
        await db.SaveChangesAsync();
    }
}
