using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Accounting.Postings;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using SiagroB1.Infra.FiscalStock;

namespace SiagroB1.Application.Services.Accounting.Offline;

/// <summary>Documentos de Entrada confirmados sem 650/660 (CTBANFE). Data = a mesma do on-line (data de estoque).</summary>
public class PurchaseInvoiceOfflineSource(IUnitOfWork db) : IOfflinePostingSource
{
    private static readonly string[] Codes = ["650", "660"];

    public OfflinePostingModule Module => OfflinePostingModule.PurchaseInvoices;

    public async IAsyncEnumerable<OfflinePostingEvent> CollectAsync(DateTime from, DateTime to, string? branchCode,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var lookup = await OfflinePostingLookup.LoadAsync(db, [PurchaseInvoicePostingContext.DocumentType], ct);
        var invoices = await db.Context.PurchaseInvoices.AsNoTracking()
            .Where(i => i.InvoiceStatus == InvoiceStatus.Confirmed || i.InvoiceStatus == InvoiceStatus.Returned ||
                        i.InvoiceStatus == InvoiceStatus.Cancelled)
            .ToListAsync(ct);

        foreach (var invoice in invoices)
        {
            var stockDate = FiscalStockPeriodGuard.StockDate(invoice);
            var date = (stockDate ?? invoice.ApprovedAt ?? DateTime.MinValue).Date;
            if (date < from.Date || date > to.Date) continue;
            var branch = invoice.BranchCode ?? string.Empty;
            if (!OfflinePostingLookup.InBranch(branch, branchCode)) continue;

            var key = invoice.Key.ToString();
            var number = invoice.TaxDocumentNumber ?? invoice.InvoiceNumber ?? string.Empty;
            if (invoice.InvoiceStatus == InvoiceStatus.Cancelled)
            {
                if (!lookup.HasAny(key))
                    yield return new OfflinePostingEvent(date, PurchaseInvoicePostingContext.DocumentType, key, number, branch, [],
                        OfflinePostingLookup.NoNetEffect, (_, _, _) => Task.FromResult<IReadOnlyList<Domain.Entities.JournalEntry>>([]));
                continue;
            }

            var missing = lookup.Missing(key, PurchaseInvoicePostingContext.ConfirmEvent, Codes);
            if (missing.Count == 0) continue;

            var invoiceKey = invoice.Key;
            yield return new OfflinePostingEvent(date, PurchaseInvoicePostingContext.DocumentType, key, number, branch, missing,
                stockDate is null ? OfflinePostingLookup.DocumentDateUsed : null,
                async (engine, user, token) =>
                {
                    var tracked = await db.Context.PurchaseInvoices.FirstAsync(i => i.Key == invoiceKey, token);
                    var lines = await db.Context.PurchaseInvoicesItems.Where(l => l.PurchaseInvoiceKey == invoiceKey).ToListAsync(token);
                    var (items, document) = await PurchaseInvoicePostingContext.BuildAsync(db, tracked, lines,
                        PurchaseInvoicePostingContext.ConfirmEvent, date);
                    return await OfflinePostingLookup.PostAllAsync(engine,
                        missing.Select(c => (c, c == "650" ? items : document)), user, token);
                });
        }
    }
}
