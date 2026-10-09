using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Application.Tests.Support;

/// <summary>Documentos mínimos para os testes dos relatórios fiscais.</summary>
public static class InvoiceReportSeed
{
    public static readonly DateTime Jul01 = new(2026, 7, 1);
    public static readonly DateTime Jul31 = new(2026, 7, 31);

    public static SalesInvoice Sale(
        string number,
        DateTime? date = null,
        InvoiceStatus? status = InvoiceStatus.Confirmed,
        SalesInvoiceType type = SalesInvoiceType.Normal,
        string branchCode = "01",
        string cardCode = "C001",
        string cardName = "COOPERATIVA CENTRAL",
        params SalesInvoiceItem[] items)
    {
        var invoice = new SalesInvoice
        {
            Key = Guid.NewGuid(),
            InvoiceNumber = number,
            InvoiceDate = date ?? new DateTime(2026, 7, 15),
            InvoiceStatus = status,
            InvoiceType = type,
            BranchCode = branchCode,
            CardCode = cardCode,
            CardName = cardName,
        };

        foreach (var item in items.Length > 0 ? items : [SaleItem()])
        {
            item.SalesInvoiceKey = invoice.Key;
            invoice.Items.Add(item);
        }

        return invoice;
    }

    public static SalesInvoiceItem SaleItem(
        string itemCode = "10001",
        string? itemName = "SOJA EM GRÃOS",
        decimal quantity = 10m,
        decimal unitPrice = 100m,
        string uom = "TN") => new()
    {
        Key = Guid.NewGuid(),
        ItemCode = itemCode,
        ItemName = itemName,
        Quantity = quantity,
        UnitPrice = unitPrice,
        UnitOfMeasureCode = uom,
    };

    public static PurchaseInvoice Purchase(
        string number,
        DateTime? date = null,
        InvoiceStatus status = InvoiceStatus.Confirmed,
        PurchaseInvoiceType type = PurchaseInvoiceType.Normal,
        DocumentIssuerType issuer = DocumentIssuerType.ThirdParty,
        string branchCode = "01",
        string cardCode = "F001",
        string cardName = "PRODUTOR RURAL",
        params PurchaseInvoiceItem[] items)
    {
        var invoice = new PurchaseInvoice
        {
            Key = Guid.NewGuid(),
            InvoiceNumber = number,
            IssueDate = date ?? new DateTime(2026, 7, 15),
            PostingDate = date ?? new DateTime(2026, 7, 16),
            InvoiceStatus = status,
            InvoiceType = type,
            IssuerType = issuer,
            BranchCode = branchCode,
            CardCode = cardCode,
            CardName = cardName,
        };

        foreach (var item in items.Length > 0 ? items : [PurchaseItem()])
        {
            item.PurchaseInvoiceKey = invoice.Key;
            invoice.Items.Add(item);
        }

        return invoice;
    }

    public static PurchaseInvoiceItem PurchaseItem(
        string? itemCode = "10001",
        string? itemName = "SOJA EM GRÃOS",
        decimal quantity = 10m,
        decimal unitPrice = 100m,
        string? uom = "TN") => new()
    {
        Key = Guid.NewGuid(),
        ItemCode = itemCode,
        ItemName = itemName,
        Quantity = quantity,
        UnitPrice = unitPrice,
        UnitOfMeasureCode = uom,
    };
}
