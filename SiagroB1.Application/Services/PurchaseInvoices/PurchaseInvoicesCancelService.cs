using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.StorageCustody;
using SiagroB1.Application.Services.StorageDeposits;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;
using SiagroB1.Infra.Queries;

namespace SiagroB1.Application.Services.PurchaseInvoices;

/// <summary>
/// Cancela o documento de entrada.
///
/// Nesta fase não há nada para estornar: o documento nunca moveu saldo, ledger ou romaneio.
/// Cancelar tira o registro da conciliação — sem apagar o documento, porque o índice único é filtrado
/// por status e o rastro do que foi lançado precisa sobreviver. A chave da NF-e de TERCEIRO volta a
/// ficar livre para relançamento; a da NF-e PRÓPRIA nunca é reaproveitada (o número é da reserva).
/// </summary>
public class PurchaseInvoicesCancelService(IUnitOfWork db)
{
    public Task ExecuteAsync(Guid key, string userName) => CancelAsync(key, userName, afterNfe: false);

    /// <summary>Fase 2 do cancelamento da NF-e: a SEFAZ já cancelou; a trava da NF-e não se aplica.</summary>
    public virtual Task CancelAfterNfeAsync(Guid key, string userName) => CancelAsync(key, userName, afterNfe: true);

    /// <summary>Ensaio antes de falar com a SEFAZ: só as regras de negócio, nada é alterado.</summary>
    public async Task EnsureCanCancelAsync(Guid key)
    {
        var invoice = await db.Context.PurchaseInvoices.AsNoTracking().FirstOrDefaultAsync(x => x.Key == key)
                      ?? throw new NotFoundException("Documento de entrada não encontrado.");

        await EnsureBusinessRulesAsync(invoice.Key, invoice.InvoiceStatus, invoice.InvoiceNumber);
    }

    private async Task CancelAsync(Guid key, string userName, bool afterNfe)
    {
        var invoice = await db.Context.PurchaseInvoices.FirstOrDefaultAsync(x => x.Key == key)
                      ?? throw new NotFoundException("Documento de entrada não encontrado.");

        if (invoice.InvoiceStatus == InvoiceStatus.Cancelled)
            throw new DefaultException("Documento de entrada já está cancelado.");

        if (afterNfe)
        {
            if (invoice.NfeStatus != NfeStatus.Cancelled)
                throw new DefaultException("A NF-e deste documento não está cancelada na SEFAZ.");
        }
        else
        {
            PurchaseInvoiceNfeLock.EnsureCancellable(invoice);
        }

        await EnsureBusinessRulesAsync(invoice.Key, invoice.InvoiceStatus, invoice.InvoiceNumber);

        invoice.InvoiceStatus = InvoiceStatus.Cancelled;
        invoice.CanceledAt = DateTime.Now;
        invoice.CanceledBy = userName;

        await StorageReturnReconciliationsService.RemoveForInvoiceAsync(db.Context, invoice.Key);
        await db.SaveChangesAsync();
    }

    private async Task EnsureBusinessRulesAsync(Guid key, InvoiceStatus status, string? invoiceNumber)
    {
        if (status == InvoiceStatus.Cancelled)
            throw new DefaultException("Documento de entrada já está cancelado.");

        // Complemento de valor próprio aponta itens desta nota (spec 2026-10-14, D4/C8). Vale para o cancelamento do
        // documento e, pelo ensaio do handler, para o da NF-e — antes de falar com a SEFAZ. Primeira das recusas: a nº 5
        // de referência da CEAGUI dev tem uma devolução Rejeitada ainda ativa, e o roteiro espera ver C8.
        await PurchaseInvoiceValueComplement.EnsureNoActiveComplementAsync(db.Context, key, invoiceNumber);

        // A devolução referencia a chave desta nota (refNFe): cancelar a origem a deixaria apontando
        // para uma NF-e cancelada. Mesma regra que a saída já tem.
        if (await db.Context.PurchaseInvoices.AnyAsync(x =>
                x.PurchaseInvoiceOriginKey == key && x.IsNfeReturn && x.InvoiceStatus != InvoiceStatus.Cancelled))
            throw new DefaultException("Documento de entrada possui devolução.");

        // A contra-nota referencia a chave desta escrituração (spec 2026-10-10 D10).
        await PurchaseInvoiceCounterNote.EnsureNoValidAsync(db.Context, key, "cancelar a escrituração");

        // Cadeia de custódia na ordem inversa (spec 2026-10-12 §4.5).
        await StorageCustodyGuard.EnsurePurchaseHasNoLaterStepAsync(db.Context, key);

        // Títulos firmes (spec 2026-10-13, regras 6-7): cancelar o documento desfaz os vínculos, e o interceptor recusa
        // se algum firme está composto ou baixado. Sem este ensaio a recusa viria no SaveChanges da fase 2, DEPOIS de
        // a SEFAZ já ter cancelado a NF-e (NfeStatus=Cancelled com o documento ainda ativo).
        var linkKeys = await db.Context.PurchaseFiscalReconciliations.AsNoTracking()
            .Where(l => l.PurchaseInvoiceItem!.PurchaseInvoiceKey == key)
            .Select(l => l.Key)
            .ToListAsync();
        foreach (var firm in PurchaseFirmPayableRules.ActiveFirmsOf(db.Context, linkKeys))
            PurchaseFirmPayableRules.EnsureLoose(db.Context, firm);

        // Spec 2026-10-13 D11: o complemento de valor tem o firme na LINHA (OriginType PurchaseInvoice); composto ou
        // baixado, recusa aqui também, antes da SEFAZ (P12).
        var lineKeys = await db.Context.PurchaseInvoicesItems.AsNoTracking()
            .Where(l => l.PurchaseInvoiceKey == key)
            .Select(l => l.Key!.Value)
            .ToListAsync();
        PurchaseFirmPayableRules.EnsureValueComplementFirmsLoose(db.Context, lineKeys);
    }
}
