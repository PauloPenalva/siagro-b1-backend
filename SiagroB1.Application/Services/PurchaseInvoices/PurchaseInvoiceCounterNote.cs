using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.PurchaseInvoices;

/// <summary>
/// Regras da contra-nota (spec 2026-10-10): a entrada própria que nasce da escrituração da NF-e do produtor e é só
/// documento para a SEFAZ. "Válida" é a que trava a escrituração e impede gerar outra (§7): não cancelada como
/// documento, e com a NF-e fora de cancelada, inutilizada ou denegada.
/// </summary>
public static class PurchaseInvoiceCounterNote
{
    public static Task<PurchaseInvoice?> FindValidAsync(AppDbContext context, Guid producerInvoiceKey) =>
        context.PurchaseInvoices.AsNoTracking()
            .Where(i => i.ProducerInvoiceKey == producerInvoiceKey
                        && i.InvoiceStatus != InvoiceStatus.Cancelled
                        && i.NfeStatus != NfeStatus.Cancelled
                        && i.NfeStatus != NfeStatus.Voided
                        && i.NfeStatus != NfeStatus.Denied)
            .FirstOrDefaultAsync();

    /// <summary>Escrituração com contra-nota válida não volta a Pendente nem é cancelada (D10).</summary>
    public static async Task EnsureNoValidAsync(AppDbContext context, Guid producerInvoiceKey, string action)
    {
        if (await FindValidAsync(context, producerInvoiceKey) is { } note)
            throw new DefaultException(
                $"A escrituração tem a contra-nota {note.InvoiceNumber}: cancele ou exclua a contra-nota antes de {action}.");
    }
}
