using SiagroB1.Application.Services.StorageCustody;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Taxes;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.PurchaseInvoices;

/// <summary>
/// Exclui uma linha do documento de entrada. Só com o documento pai PENDENTE — ver
/// <see cref="PurchaseInvoiceLineGuard"/>.
/// </summary>
public class PurchaseInvoicesItemsDeleteService(IUnitOfWork db, PurchaseInvoicesTaxApplyService taxApply)
{
    public async Task ExecuteAsync(Guid key)
    {
        var item = await db.Context.PurchaseInvoicesItems
                       .FirstOrDefaultAsync(x => x.Key == key)
                   ?? throw new NotFoundException("Linha do documento de entrada não encontrada.");

        await PurchaseInvoiceLineGuard.EnsureParentIsPendingAsync(db, item.PurchaseInvoiceKey);

        var invoice = await db.Context.PurchaseInvoices.FirstAsync(x => x.Key == item.PurchaseInvoiceKey);
        PurchaseInvoiceNfeLock.EnsureLinesRemovable(invoice);
        await PurchaseInvoiceCounterNote.EnsureLinesNotReferencedAsync(db.Context, [item]);

        // Valor declarado do terceiro Normal: a soma das linhas que ficam, como na emissão própria.
        if (PurchaseInvoiceDeclaredTotal.AppliesTo(invoice) && await taxApply.IsBranchActiveAsync(invoice.BranchCode))
            await PurchaseInvoiceDeclaredTotal.ApplyAsync(db, invoice, item.Key, null);

        // A linha que sai não pode deixar o romaneio de custódia sem nota (spec 2026-10-12 §3.3).
        var remaining = await db.Context.PurchaseInvoicesItems.AsNoTracking()
            .Where(x => x.PurchaseInvoiceKey == item.PurchaseInvoiceKey && x.Key != item.Key).ToListAsync();
        await StorageCustodyGuard.EnsurePurchaseAsync(db.Context, invoice, remaining);

        // Retenções de serviço (spec 2026-10-17 §2): a base muda com a linha que sai.
        await taxApply.ApplyWithholdingsAsync(invoice, remaining);
        await taxApply.ApplyDifalAsync(invoice, remaining);

        db.Context.PurchaseInvoicesItems.Remove(item);
        await db.SaveChangesAsync();
    }
}
