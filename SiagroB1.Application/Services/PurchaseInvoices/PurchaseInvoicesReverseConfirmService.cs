using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Accounting.Postings;
using SiagroB1.Application.Services.Financials;
using SiagroB1.Application.Services.Nfe;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;
using SiagroB1.Infra.FiscalStock;

namespace SiagroB1.Application.Services.PurchaseInvoices;

/// <summary>
/// Estorna a confirmação, devolvendo o documento a pendente para poder ser corrigido.
///
/// Reabre a edição e LIMPA o carimbo de aprovação: documento pendente com aprovador preenchido mente na tela e no
/// relatório. O único efeito a desfazer é o da "outra compra" (spec 2026-10-17 §3): as parcelas e as retenções nascidas na
/// confirmação são canceladas no mesmo SaveChanges, e recusadas se já tiverem baixa. A confirmação é idempotente por
/// título ativo, então sem este cancelamento um estorno + edição + nova confirmação manteria títulos velhos.
/// </summary>
public class PurchaseInvoicesReverseConfirmService(
    IUnitOfWork db,
    FinancialDocumentsOtherPurchaseService otherPurchaseTitles,
    FiscalStockPeriodGuard? periodGuard = null,
    IAccountingPostingService? accounting = null)
{
    private readonly FiscalStockPeriodGuard _periodGuard = periodGuard ?? new FiscalStockPeriodGuard(db);

    public async Task ExecuteAsync(Guid key, string userName)
    {
        var invoice = await db.Context.PurchaseInvoices
                          .FirstOrDefaultAsync(x => x.Key == key)
                      ?? throw new NotFoundException("Documento de entrada não encontrado.");

        // NF-e emitida continua valendo na SEFAZ: desfazer a confirmação deixaria a nota
        // sem efeito no sistema. O caminho é o cancelamento da NF-e.
        if (invoice.NfeStatus is NfeStatus.Processing or NfeStatus.Authorized or NfeStatus.Cancelled)
            throw new DefaultException(NfeLockRules.EmittedReverseMessage);

        if (invoice.InvoiceStatus != InvoiceStatus.Confirmed)
            throw new DefaultException("Somente documento confirmado pode ser estornado.");

        // O estorno tira a entrada do estoque fiscal do mês gravado: mês fechado recusa.
        await _periodGuard.EnsureOpenAsync(invoice, FiscalStockPeriodGuard.StockDate(invoice));

        // A contra-nota referencia a chave desta escrituração (spec 2026-10-10 D10).
        await PurchaseInvoiceCounterNote.EnsureNoValidAsync(db.Context, invoice.Key, "estornar a confirmação");

        await otherPurchaseTitles.EnsureCanUndoAsync(invoice.Key);
        otherPurchaseTitles.EnqueueUndo(invoice.Key, userName);

        // Estorno da contabilização (655 itens / 665 total); sem lançamento da confirmação, não faz nada.
        if (accounting is not null)
        {
            var postingLines = await db.Context.PurchaseInvoicesItems.Where(l => l.PurchaseInvoiceKey == invoice.Key).ToListAsync();
            var (itemsCtx, documentCtx) = await PurchaseInvoicePostingContext.BuildAsync(db, invoice, postingLines,
                PurchaseInvoicePostingContext.UndoEvent, DateTime.Today, false);
            await accounting.PostReversalAsync("655", itemsCtx, PurchaseInvoicePostingContext.ConfirmEvent, userName);
            await accounting.PostReversalAsync("665", documentCtx, PurchaseInvoicePostingContext.ConfirmEvent, userName);
        }

        invoice.InvoiceStatus = InvoiceStatus.Pending;
        invoice.ApprovedAt = null;
        invoice.ApprovedBy = null;
        invoice.UpdatedAt = DateTime.Now;
        invoice.UpdatedBy = userName;

        await db.SaveChangesAsync();
    }
}
