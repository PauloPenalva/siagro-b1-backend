using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

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

        await EnsureBusinessRulesAsync(invoice.Key, invoice.InvoiceStatus);
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

        await EnsureBusinessRulesAsync(invoice.Key, invoice.InvoiceStatus);

        invoice.InvoiceStatus = InvoiceStatus.Cancelled;
        invoice.CanceledAt = DateTime.Now;
        invoice.CanceledBy = userName;

        await db.SaveChangesAsync();
    }

    private async Task EnsureBusinessRulesAsync(Guid key, InvoiceStatus status)
    {
        if (status == InvoiceStatus.Cancelled)
            throw new DefaultException("Documento de entrada já está cancelado.");

        // A devolução referencia a chave desta nota (refNFe): cancelar a origem a deixaria apontando
        // para uma NF-e cancelada. Mesma regra que a saída já tem.
        if (await db.Context.PurchaseInvoices.AnyAsync(x =>
                x.PurchaseInvoiceOriginKey == key && x.IsNfeReturn && x.InvoiceStatus != InvoiceStatus.Cancelled))
            throw new DefaultException("Documento de entrada possui devolução.");
    }
}
