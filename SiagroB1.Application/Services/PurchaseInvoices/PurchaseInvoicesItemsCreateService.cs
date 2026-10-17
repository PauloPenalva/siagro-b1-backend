using SiagroB1.Application.Services.StorageCustody;
using SiagroB1.Application.Services.StorageDeposits;
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
        // Frete, seguro, desconto e outras despesas da linha (spec 2026-10-05 §5).
        InvoiceLineChargeRules.Ensure(item);

        // O vínculo com a linha da escrituração só nasce no "Gerar contra-nota" (spec 2026-10-10 §4).
        item.ProducerInvoiceItemKey = null;

        await PurchaseInvoiceLineGuard.EnsureParentIsPendingAsync(db, item.PurchaseInvoiceKey);

        var invoice = await db.Context.PurchaseInvoices.FirstAsync(x => x.Key == item.PurchaseInvoiceKey);

        item.ItemName = await PurchaseInvoiceLineGuard.ResolveItemNameAsync(
            itemService, item.ItemCode, item.ItemName,
            otherPurchase: await taxApply.IsOtherPurchaseLineAsync(invoice, item));

        PurchaseInvoiceNfeLock.EnsureLineCanBeAdded(invoice);

        // Linha incluída depois da importação não é item da nota do fornecedor: sem nItem (informado no "Devolver").
        // A tributação vem da natureza, como em toda linha do terceiro Normal.
        // Só na filial que emite NF-e pelo Siagro (regra ativa); fora dela a linha grava como veio.
        if (invoice.IssuerType == DocumentIssuerType.ThirdParty && invoice.InvoiceType == PurchaseInvoiceType.Normal &&
            await taxApply.IsBranchActiveAsync(invoice.BranchCode))
            item.NfeItemNumber = null;

        // A natureza decide se a linha exige ou recusa o contrato, sobre as linhas finais do documento (spec 2026-10-17 §1).
        var existingLines = await db.Context.PurchaseInvoicesItems.AsNoTracking()
            .Where(x => x.PurchaseInvoiceKey == item.PurchaseInvoiceKey).ToListAsync();
        await taxApply.EnsureUsageContractRuleAsync(invoice, [.. existingLines, item]);

        // Linha sem contrato (caso comum: insumo, serviço, frete) não precisa chamar o guard. Linha do complemento próprio:
        // a regra do complemento valida o contrato depois de herdar o produto e de C3 (spec 2026-10-14, D5).
        if (item.PurchaseContractKey is not null && item.ComplementedItemKey is null)
            await PurchaseInvoiceLineGuard.EnsureContractIsCompatibleAsync(
                db, item.PurchaseContractKey, item.ItemCode, invoice.CardCode);

        // Retorno de depósito: confere a linha nova junto das demais do documento (spec 2026-10-10 §3.3).
        var others = existingLines;
        PurchaseInvoiceLineGuard.EnsureUnitOfMeasure(invoice, [.. others, item]);
        await taxApply.EnsureValueComplementAsync(invoice, [.. others, item]);
        await StorageDepositReturnGuard.EnsureAsync(db.Context, invoice.CardCode, item.PurchaseInvoiceKey, [.. others, item]);
        await StorageCustodyGuard.EnsurePurchaseAsync(db.Context, invoice, [.. others, item]);

        // Funrural é calculado, nunca digitado (ver PurchaseInvoicesCreateService).
        PurchaseInvoiceFunrural.Clear(item);

        // Tributos pela natureza (no-op com a regra inativa, devolução do cliente ou documento confirmado).
        await taxApply.ApplyAsync(invoice, [item]);

        // Valor declarado do terceiro Normal: a soma das linhas, como na emissão própria (o da tela não vale).
        if (PurchaseInvoiceDeclaredTotal.AppliesTo(invoice) && await taxApply.IsBranchActiveAsync(invoice.BranchCode))
            await PurchaseInvoiceDeclaredTotal.ApplyAsync(db, invoice, item.Key, item);

        // Retenções de serviço (spec 2026-10-17 §2): a base muda com a linha nova.
        await taxApply.ApplyWithholdingsAsync(invoice, [.. existingLines, item]);

        await db.Context.PurchaseInvoicesItems.AddAsync(item);
        await db.SaveChangesAsync();
    }
}
