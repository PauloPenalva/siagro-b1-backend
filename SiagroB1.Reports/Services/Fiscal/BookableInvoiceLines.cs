using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Reports.Services.Fiscal;

/// <summary>
/// Regra única de "linha escriturável" dos relatórios fiscais (Conferência, Kardex): documento
/// confirmado/retornado, NF-e ou documento comum, NF-e não cancelada/denegada/inutilizada, com
/// documento fiscal e — se NF-e PRÓPRIA — autorizada. Não filtra período nem filial.
/// </summary>
public static class BookableInvoiceLines
{
    /// <summary>NF-e que não vale como documento escriturável.</summary>
    private static readonly NfeStatus[] VoidNfeStatuses = [NfeStatus.Cancelled, NfeStatus.Denied, NfeStatus.Voided];

    private static readonly InvoiceStatus[] BookableStatuses = [InvoiceStatus.Confirmed, InvoiceStatus.Returned];

    private static readonly TaxDocumentKind[] BookableKinds = [TaxDocumentKind.Nfe, TaxDocumentKind.Other];

    /// <summary>Linhas de saída que valem como documento, inclusive NF-e própria ainda não autorizada.</summary>
    private static IQueryable<SalesInvoiceItem> SalesValid(IQueryable<SalesInvoiceItem> source) => source
        .Where(i => i.SalesInvoice != null)
        .Where(i => i.SalesInvoice!.InvoiceStatus != null && BookableStatuses.Contains(i.SalesInvoice.InvoiceStatus.Value))
        .Where(i => !i.SalesInvoice!.WithoutTaxDocument)
        .Where(i => BookableKinds.Contains(i.SalesInvoice!.TaxDocumentKind))
        .Where(i => !VoidNfeStatuses.Contains(i.SalesInvoice!.NfeStatus));

    private static IQueryable<PurchaseInvoiceItem> PurchasesValid(IQueryable<PurchaseInvoiceItem> source) => source
        .Where(i => i.PurchaseInvoice != null)
        .Where(i => BookableStatuses.Contains(i.PurchaseInvoice!.InvoiceStatus))
        .Where(i => BookableKinds.Contains(i.PurchaseInvoice!.TaxDocumentKind))
        .Where(i => !VoidNfeStatuses.Contains(i.PurchaseInvoice!.NfeStatus));

    /// <summary>Saídas escrituráveis: NF-e (toda saída é própria) só se autorizada.</summary>
    public static IQueryable<SalesInvoiceItem> Sales(IQueryable<SalesInvoiceItem> source) => SalesValid(source)
        .Where(i => i.SalesInvoice!.TaxDocumentKind != TaxDocumentKind.Nfe
                 || i.SalesInvoice.NfeStatus == NfeStatus.Authorized);

    /// <summary>Entradas escrituráveis: NF-e PRÓPRIA só se autorizada; de terceiro não depende.</summary>
    public static IQueryable<PurchaseInvoiceItem> Purchases(IQueryable<PurchaseInvoiceItem> source) => PurchasesValid(source)
        .Where(i => i.PurchaseInvoice!.IssuerType != DocumentIssuerType.Own
                 || i.PurchaseInvoice.TaxDocumentKind != TaxDocumentKind.Nfe
                 || i.PurchaseInvoice.NfeStatus == NfeStatus.Authorized);

    /// <summary>Saídas de NF-e ainda não autorizada (a Conferência lista como inconsistência).</summary>
    public static IQueryable<SalesInvoiceItem> UnauthorizedOwnSales(IQueryable<SalesInvoiceItem> source) => SalesValid(source)
        .Where(i => i.SalesInvoice!.TaxDocumentKind == TaxDocumentKind.Nfe
                 && i.SalesInvoice.NfeStatus != NfeStatus.Authorized);

    /// <summary>Entradas de NF-e própria ainda não autorizada.</summary>
    public static IQueryable<PurchaseInvoiceItem> UnauthorizedOwnPurchases(IQueryable<PurchaseInvoiceItem> source) => PurchasesValid(source)
        .Where(i => i.PurchaseInvoice!.IssuerType == DocumentIssuerType.Own
                 && i.PurchaseInvoice.TaxDocumentKind == TaxDocumentKind.Nfe
                 && i.PurchaseInvoice.NfeStatus != NfeStatus.Authorized);
}
