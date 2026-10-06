using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Nfe;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.PurchaseInvoices;

/// <summary>
/// Estorna a confirmação, devolvendo o documento a pendente para poder ser corrigido.
///
/// Nesta fase não há efeito a desfazer — ver <see cref="PurchaseInvoicesConfirmService"/>. O que o
/// estorno faz é reabrir a edição e LIMPAR o carimbo de aprovação: documento pendente com
/// aprovador preenchido mente na tela e no relatório.
/// </summary>
public class PurchaseInvoicesReverseConfirmService(IUnitOfWork db)
{
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

        invoice.InvoiceStatus = InvoiceStatus.Pending;
        invoice.ApprovedAt = null;
        invoice.ApprovedBy = null;
        invoice.UpdatedAt = DateTime.Now;
        invoice.UpdatedBy = userName;

        await db.SaveChangesAsync();
    }
}
