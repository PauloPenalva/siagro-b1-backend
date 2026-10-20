using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra.Context;

namespace SiagroB1.Infra.Queries;

/// <summary>Número e série da NF-e (ou nota de papel) do documento fiscal de um título; vazios quando não há.</summary>
public sealed record InvoiceNumberSeries(string Number, string Series)
{
    public static readonly InvoiceNumberSeries Empty = new(string.Empty, string.Empty);
}

/// <summary>
/// Nota fiscal de cada título financeiro, para os relatórios (Carteira, Baixas, Extrato). Fonte: PurchaseInvoiceKey →
/// PURCHASE_INVOICES; senão SalesInvoiceKey → SALES_INVOICES; senão, só nas origens em que OriginKey É a chave do documento
/// (OtherPurchaseInvoice, SalesInvoiceNoContract), o OriginKey. Nas origens PurchaseInvoice/SalesInvoice o OriginKey é a
/// LINHA do documento, por isso não serve de fallback. Provisório, adiantamento, manual e washout não têm nota.
/// Uma consulta por tipo de documento, as duas tabelas no banco da aplicação (vale em STANDALONE e SAPB1).
/// </summary>
public sealed class FinancialDocumentInvoiceLookup
{
    private readonly Dictionary<Guid, InvoiceNumberSeries> _purchase;
    private readonly Dictionary<Guid, InvoiceNumberSeries> _sales;

    private FinancialDocumentInvoiceLookup(Dictionary<Guid, InvoiceNumberSeries> purchase, Dictionary<Guid, InvoiceNumberSeries> sales)
    {
        _purchase = purchase;
        _sales = sales;
    }

    public static async Task<FinancialDocumentInvoiceLookup> LoadAsync(AppDbContext ctx, IEnumerable<FinancialDocument> documents,
        CancellationToken ct)
    {
        var purchaseKeys = new HashSet<Guid>();
        var salesKeys = new HashSet<Guid>();
        foreach (var d in documents)
        {
            switch (Source(d))
            {
                case (true, var key): purchaseKeys.Add(key); break;
                case (false, var key) when key != Guid.Empty: salesKeys.Add(key); break;
            }
        }

        var purchase = new Dictionary<Guid, InvoiceNumberSeries>();
        if (purchaseKeys.Count > 0)
        {
            var keys = purchaseKeys.ToList();
            foreach (var i in await ctx.PurchaseInvoices.AsNoTracking()
                         .Where(i => EF.Parameter(keys).Contains(i.Key))
                         .Select(i => new { i.Key, i.TaxDocumentNumber, i.TaxDocumentSeries }).ToListAsync(ct))
                purchase[i.Key] = Of(i.TaxDocumentNumber, i.TaxDocumentSeries);
        }

        var sales = new Dictionary<Guid, InvoiceNumberSeries>();
        if (salesKeys.Count > 0)
        {
            var keys = salesKeys.ToList();
            foreach (var i in await ctx.SalesInvoices.AsNoTracking()
                         .Where(i => EF.Parameter(keys).Contains(i.Key))
                         .Select(i => new { i.Key, i.TaxDocumentNumber, i.TaxDocumentSeries }).ToListAsync(ct))
                sales[i.Key] = Of(i.TaxDocumentNumber, i.TaxDocumentSeries);
        }

        return new FinancialDocumentInvoiceLookup(purchase, sales);
    }

    public InvoiceNumberSeries For(FinancialDocument document) => Source(document) switch
    {
        (true, var key) => _purchase.GetValueOrDefault(key) ?? InvoiceNumberSeries.Empty,
        (false, var key) when key != Guid.Empty => _sales.GetValueOrDefault(key) ?? InvoiceNumberSeries.Empty,
        _ => InvoiceNumberSeries.Empty
    };

    /// <summary>(IsPurchase, Key); Key vazio quando o título não tem documento fiscal.</summary>
    private static (bool IsPurchase, Guid Key) Source(FinancialDocument d)
    {
        if (d.Nature is FinancialDocumentNature.Provisional or FinancialDocumentNature.Advance) return (false, Guid.Empty);
        if (d.PurchaseInvoiceKey is { } purchase) return (true, purchase);
        if (d.SalesInvoiceKey is { } sales) return (false, sales);
        return d.OriginType switch
        {
            FinancialDocumentOrigin.OtherPurchaseInvoice when d.OriginKey is { } k => (true, k),
            FinancialDocumentOrigin.SalesInvoiceNoContract when d.OriginKey is { } k => (false, k),
            _ => (false, Guid.Empty)
        };
    }

    // Sem número não há nota a mostrar: série sozinha confunde.
    private static InvoiceNumberSeries Of(string? number, string? series) => string.IsNullOrWhiteSpace(number)
        ? InvoiceNumberSeries.Empty
        : new InvoiceNumberSeries(number.Trim(), series?.Trim() ?? string.Empty);
}
