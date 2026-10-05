using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.PurchaseInvoices;

/// <summary>
/// Inclui uma linha no documento de entrada.
///
/// A guarda de status é aplicada aqui e não só no Update do cabeçalho — ver
/// <see cref="PurchaseInvoiceLineGuard"/>.
/// </summary>
public class PurchaseInvoicesItemsCreateService(
    IUnitOfWork db,
    IItemService itemService,
    PurchaseInvoicesTaxApplyService taxApply)
{
    public async Task ExecuteAsync(PurchaseInvoiceItem item, string userName)
    {
        await PurchaseInvoiceLineGuard.EnsureParentIsPendingAsync(db, item.PurchaseInvoiceKey);

        item.ItemName = await PurchaseInvoiceLineGuard.ResolveItemNameAsync(
            itemService, item.ItemCode, item.ItemName);

        var invoice = await db.Context.PurchaseInvoices.FirstAsync(x => x.Key == item.PurchaseInvoiceKey);

        PurchaseInvoiceNfeLock.EnsureLineCanBeAdded(invoice);

        // Linha incluída depois da importação não é item da nota do fornecedor: sem nItem (informado no "Devolver").
        // A tributação vem da natureza, como em toda linha do terceiro Normal.
        // Só na filial que emite NF-e pelo Siagro (regra ativa); fora dela a linha grava como veio.
        if (invoice.IssuerType == DocumentIssuerType.ThirdParty && invoice.InvoiceType == PurchaseInvoiceType.Normal &&
            await taxApply.IsBranchActiveAsync(invoice.BranchCode))
            item.NfeItemNumber = null;

        // Linha sem contrato (caso comum: insumo, serviço, frete) não precisa chamar o guard.
        if (item.PurchaseContractKey is not null)
            await PurchaseInvoiceLineGuard.EnsureContractIsCompatibleAsync(
                db, item.PurchaseContractKey, item.ItemCode, invoice.CardCode);

        // Tributos pela natureza (no-op com a regra inativa, devolução do cliente ou documento confirmado).
        await taxApply.ApplyAsync(invoice, [item]);

        // Valor declarado do terceiro Normal: a soma das linhas, como na emissão própria (o da tela não vale).
        if (PurchaseInvoiceDeclaredTotal.AppliesTo(invoice) && await taxApply.IsBranchActiveAsync(invoice.BranchCode))
            await PurchaseInvoiceDeclaredTotal.ApplyAsync(db, invoice, item.Key, item);

        await db.Context.PurchaseInvoicesItems.AddAsync(item);
        await db.SaveChangesAsync();
    }
}
