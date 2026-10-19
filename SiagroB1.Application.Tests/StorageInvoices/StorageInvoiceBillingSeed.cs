using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.StorageInvoices;

/// <summary>Fatura de armazenagem F1 (C0001, total 1.000,00) e documentos de saída/títulos ligados a ela, gravados direto.</summary>
internal sealed class StorageInvoiceBillingSeed
{
    public UnitOfWork Db { get; } = TestDb.CreateUnitOfWork();
    public StorageInvoice Invoice { get; private set; } = null!;

    public static async Task<StorageInvoiceBillingSeed> CreateAsync(
        decimal total = 1000m, StorageInvoiceStatus status = StorageInvoiceStatus.Open)
    {
        var seed = new StorageInvoiceBillingSeed();
        seed.Invoice = new StorageInvoice
        {
            Key = Guid.NewGuid(), Code = "F1", StorageAddressCode = "LOTE1", CardCode = "C0001", CardName = "CLIENTE",
            PeriodStart = new DateTime(2026, 9, 1), PeriodEnd = new DateTime(2026, 9, 30), Status = status, TotalAmount = total,
        };
        seed.Db.Context.StorageInvoices.Add(seed.Invoice);
        await seed.Db.Context.SaveChangesAsync();
        return seed;
    }

    public async Task<SalesInvoice> AddDocumentAsync(decimal amount, InvoiceStatus status = InvoiceStatus.Confirmed,
        TaxDocumentKind kind = TaxDocumentKind.Nfse, string number = "DS1")
    {
        var invoice = new SalesInvoice
        {
            Key = Guid.NewGuid(), CardCode = "C0001", BranchCode = "01", InvoiceNumber = number, InvoiceStatus = status,
            TaxDocumentKind = kind, StorageInvoiceKey = Invoice.Key, InvoiceDate = new DateTime(2026, 10, 19),
        };
        invoice.Items.Add(new SalesInvoiceItem
        {
            Key = Guid.NewGuid(), SalesInvoiceKey = invoice.Key, ItemCode = "SERV", UnitOfMeasureCode = "UN",
            Quantity = 1m, UnitPrice = amount,
        });
        Db.Context.SalesInvoices.Add(invoice);
        await Db.Context.SaveChangesAsync();
        return invoice;
    }

    public async Task<FinancialDocument> AddTitleAsync(SalesInvoice invoice, decimal amount,
        FinancialDocumentStatus status = FinancialDocumentStatus.Open)
    {
        var title = new FinancialDocument
        {
            Key = Guid.NewGuid(), Code = $"T{Guid.NewGuid():N}"[..10], CardCode = "C0001", BranchCode = "01",
            Direction = FinancialDirection.Receivable, Nature = FinancialDocumentNature.Firm, Status = status,
            DueDate = new DateTime(2026, 11, 19), NetAmount = amount, Currency = CurrencyType.Brl,
            OriginType = FinancialDocumentOrigin.SalesInvoiceNoContract, OriginKey = invoice.Key, SalesInvoiceKey = invoice.Key,
            SettledAmount = status == FinancialDocumentStatus.Settled ? amount : 0m,
        };
        Db.Context.FinancialDocuments.Add(title);
        await Db.Context.SaveChangesAsync();
        return title;
    }
}
