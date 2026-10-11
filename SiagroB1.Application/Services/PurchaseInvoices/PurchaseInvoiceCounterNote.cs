using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Nfe;
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

    /// <summary>
    /// A contra-nota guarda o vínculo com a escrituração mesmo cancelada — é registro fiscal (D11) — e as FKs são
    /// Restrict: sem esta recusa, excluir a escrituração que voltou a Pendente estouraria 547 no banco real (o InMemory
    /// dos testes não acusa). Vale para qualquer contra-nota, válida ou não.
    /// </summary>
    public static async Task EnsureNotReferencedAsync(AppDbContext context, Guid invoiceKey)
    {
        if (await context.PurchaseInvoices.AnyAsync(i => i.ProducerInvoiceKey == invoiceKey))
            throw new DefaultException(
                "A escrituração tem contra-nota emitida a partir dela: cancele o documento em vez de excluí-lo.");
    }

    /// <summary>Mesma regra para a linha: a linha da contra-nota aponta para a da escrituração (Restrict).</summary>
    public static async Task EnsureLinesNotReferencedAsync(AppDbContext context, IEnumerable<PurchaseInvoiceItem> lines)
    {
        var byKey = lines.Where(l => l.Key is not null).ToDictionary(l => l.Key!.Value);
        if (byKey.Count == 0)
            return;

        var keys = byKey.Keys.ToList();
        var referenced = await context.PurchaseInvoicesItems
            .Where(i => i.ProducerInvoiceItemKey != null && keys.Contains(i.ProducerInvoiceItemKey.Value))
            .Select(i => i.ProducerInvoiceItemKey!.Value)
            .FirstOrDefaultAsync();

        if (referenced != Guid.Empty)
            throw new DefaultException(
                $"O item {byKey[referenced].ItemCode} foi reproduzido numa contra-nota e não pode ser excluído.");
    }

    /// <summary>
    /// A escrituração conferida na emissão da contra-nota, antes de reservar o número: precisa continuar confirmada e com
    /// a chave — é ela que vai no NFref.
    /// </summary>
    public static async Task<NfeCounterNoteOrigin> LoadIssueOriginAsync(AppDbContext context, Guid producerInvoiceKey)
    {
        var producer = await context.PurchaseInvoices.AsNoTracking().Include(i => i.Items)
            .FirstOrDefaultAsync(i => i.Key == producerInvoiceKey);

        if (producer is null || producer.InvoiceStatus != InvoiceStatus.Confirmed ||
            producer.ChaveNFe is not { Length: 44 } key || !key.All(char.IsAsciiDigit))
            throw new DefaultException(
                "A escrituração da NF-e do produtor precisa estar confirmada e com a chave de 44 dígitos para emitir a contra-nota.");

        return new NfeCounterNoteOrigin(key, PurchaseInvoiceFunrural.Text(producer));
    }
}
