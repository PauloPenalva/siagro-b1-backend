using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.PurchaseInvoices;

/// <summary>
/// Cancela o documento de entrada.
///
/// Nesta fase não há nada para estornar: o documento nunca moveu saldo, ledger ou romaneio.
/// Cancelar tira o registro da conciliação e LIBERA a chave de NF-e para relançamento — sem apagar
/// o documento, porque o índice único é filtrado por status e o rastro do que foi lançado precisa
/// sobreviver.
/// </summary>
public class PurchaseInvoicesCancelService(IUnitOfWork db)
{
    public async Task ExecuteAsync(Guid key, string userName)
    {
        var invoice = await db.Context.PurchaseInvoices
                          .FirstOrDefaultAsync(x => x.Key == key)
                      ?? throw new NotFoundException("Documento de entrada não encontrado.");

        if (invoice.InvoiceStatus == InvoiceStatus.Cancelled)
            throw new DefaultException("Documento de entrada já está cancelado.");

        invoice.InvoiceStatus = InvoiceStatus.Cancelled;
        invoice.CanceledAt = DateTime.Now;
        invoice.CanceledBy = userName;

        await db.SaveChangesAsync();
    }
}
