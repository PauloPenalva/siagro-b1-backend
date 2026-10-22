using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Accounting.Postings;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Accounting.Offline;

/// <summary>Documentos de Saída confirmados (ou devolvidos) sem 610/620 (CTBANFS). Data = data do documento.</summary>
public class SalesInvoiceOfflineSource(IUnitOfWork db) : IOfflinePostingSource
{
    private static readonly string[] Codes = ["610", "620"];

    public OfflinePostingModule Module => OfflinePostingModule.SalesInvoices;

    public async IAsyncEnumerable<OfflinePostingEvent> CollectAsync(DateTime from, DateTime to, string? branchCode,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var lookup = await OfflinePostingLookup.LoadAsync(db, [SalesInvoicePostingContext.DocumentType], ct);
        var invoices = await db.Context.SalesInvoices.AsNoTracking()
            .Where(i => i.InvoiceStatus == InvoiceStatus.Confirmed || i.InvoiceStatus == InvoiceStatus.Returned ||
                        i.InvoiceStatus == InvoiceStatus.Cancelled)
            .ToListAsync(ct);

        foreach (var invoice in invoices)
        {
            var stockDate = invoice.InvoiceDate;
            var date = (stockDate ?? invoice.ApprovedAt ?? DateTime.MinValue).Date;
            if (date < from.Date || date > to.Date) continue;
            var branch = invoice.BranchCode ?? string.Empty;
            if (!OfflinePostingLookup.InBranch(branch, branchCode)) continue;

            var key = invoice.Key.ToString();
            var number = invoice.TaxDocumentNumber ?? invoice.InvoiceNumber ?? string.Empty;
            if (invoice.InvoiceStatus == InvoiceStatus.Cancelled)
            {
                if (!lookup.HasAny(key))
                    yield return new OfflinePostingEvent(date, SalesInvoicePostingContext.DocumentType, key, number, branch, [],
                        OfflinePostingLookup.NoNetEffect, (_, _, _) => Task.FromResult<IReadOnlyList<Domain.Entities.JournalEntry>>([]));
                continue;
            }

            var missing = lookup.Missing(key, SalesInvoicePostingContext.ConfirmEvent, Codes);
            if (missing.Count == 0) continue;

            var invoiceKey = invoice.Key;
            yield return new OfflinePostingEvent(date, SalesInvoicePostingContext.DocumentType, key, number, branch, missing,
                stockDate is null ? OfflinePostingLookup.DocumentDateUsed : null,
                async (engine, user, token) =>
                {
                    var tracked = await db.Context.SalesInvoices.FirstAsync(i => i.Key == invoiceKey, token);
                    var lines = await db.Context.SalesInvoicesItems.Where(l => l.SalesInvoiceKey == invoiceKey).ToListAsync(token);
                    var (items, document) = await SalesInvoicePostingContext.BuildAsync(db, tracked, lines,
                        SalesInvoicePostingContext.ConfirmEvent, date);
                    return await OfflinePostingLookup.PostAllAsync(engine,
                        missing.Select(c => (c, c == "610" ? items : document)), user, token);
                });
        }
    }
}
