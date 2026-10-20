using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.PurchaseInvoices;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.PurchaseInvoices;

/// <summary>Vínculo do Documento de Entrada com o DF-e recebido (manifestação do destinatário).</summary>
public class PurchaseInvoicesCreateReceivedDfeLinkTests
{
    private const string Key = "35261000052998224725550010000004561123456789";

    private static (UnitOfWork Db, PurchaseInvoicesCreateService Create) Services()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.Branchs.Add(new Branch
        {
            Code = "01", BranchName = "MATRIZ", ShortName = "MATRIZ", TaxId = "12.345.678/0001-95", IssuesNfe = false,
        });
        db.Context.SaveChanges();
        var create = new PurchaseInvoicesCreateService(db, PurchaseNfeTestSeed.Partners(), new FakeItemService(),
            TaxTestServices.InactivePurchaseApply(db), new FakeDocNumberSequenceService());
        return (db, create);
    }

    private static PurchaseInvoice Invoice(int rowId = 42, string branch = "01", string key = Key)
    {
        var invoice = new PurchaseInvoice
        {
            // O InMemory não gera o RowId (identidade no SQL Server); o número do documento vem fixo no teste.
            Key = Guid.NewGuid(), RowId = rowId, BranchCode = branch, CardCode = PurchaseNfeTestSeed.Supplier,
            IssuerType = DocumentIssuerType.ThirdParty, InvoiceType = PurchaseInvoiceType.Normal,
            TaxDocumentKind = TaxDocumentKind.Nfe, ChaveNFe = key, TaxDocumentNumber = "456", TaxDocumentSeries = "1",
            IssueDate = new DateTime(2026, 10, 1),
        };
        invoice.AddItem(new PurchaseInvoiceItem
        {
            Key = Guid.NewGuid(), ItemCode = "TRIGO", UnitOfMeasureCode = "KG", Quantity = 1m, UnitPrice = 500m,
        });
        return invoice;
    }

    private static async Task SeedDfeAsync(UnitOfWork db, string branch = "01")
    {
        db.Context.ReceivedDfes.Add(new ReceivedDfe { BranchCode = branch, AccessKey = Key, CreatedAt = DateTimeOffset.Now });
        await db.SaveChangesAsync();
    }

    /// <summary>Documento já gravado e DF-e já apontando para ele — independe do create.</summary>
    private static async Task<PurchaseInvoice> SeedLinkedInvoiceAsync(UnitOfWork db)
    {
        var invoice = Invoice();
        invoice.InvoiceStatus = InvoiceStatus.Pending;
        db.Context.PurchaseInvoices.Add(invoice);
        db.Context.ReceivedDfes.Add(new ReceivedDfe
        {
            BranchCode = "01", AccessKey = Key, PurchaseInvoiceDocEntry = invoice.RowId, CreatedAt = DateTimeOffset.Now,
        });
        await db.SaveChangesAsync();
        return invoice;
    }

    [Fact]
    public async Task Create_WithKeyOfReceivedDfe_LinksDocEntry()
    {
        var (db, create) = Services();
        await SeedDfeAsync(db);
        var invoice = Invoice();

        await create.ExecuteAsync(invoice, "ana");

        var saved = await db.Context.PurchaseInvoices.AsNoTracking().SingleAsync(x => x.Key == invoice.Key);
        var dfe = await db.Context.ReceivedDfes.AsNoTracking().SingleAsync();
        Assert.Equal(42, saved.RowId);
        Assert.Equal(42, dfe.PurchaseInvoiceDocEntry);
    }

    [Fact]
    public async Task Create_WithKeyOfReceivedDfeFromOtherBranch_DoesNotLink()
    {
        var (db, create) = Services();
        await SeedDfeAsync(db, "02");

        await create.ExecuteAsync(Invoice(), "ana");

        Assert.Null((await db.Context.ReceivedDfes.AsNoTracking().SingleAsync()).PurchaseInvoiceDocEntry);
    }

    [Fact]
    public async Task Create_SecondEntryForSameKey_IsRejected()
    {
        var (db, create) = Services();
        await SeedDfeAsync(db);
        await create.ExecuteAsync(Invoice(), "ana");

        await Assert.ThrowsAsync<DefaultException>(() => create.ExecuteAsync(Invoice(43), "ana"));

        Assert.Equal(1, await db.Context.PurchaseInvoices.CountAsync());
    }

    [Fact]
    public async Task Delete_LinkedPurchaseInvoice_ClearsReceivedDfeLink()
    {
        var (db, _) = Services();
        var invoice = await SeedLinkedInvoiceAsync(db);

        await new PurchaseInvoicesDeleteService(db).ExecuteAsync(invoice.Key);

        Assert.Null((await db.Context.ReceivedDfes.AsNoTracking().SingleAsync()).PurchaseInvoiceDocEntry);
        Assert.Empty(await db.Context.PurchaseInvoices.ToListAsync());
    }

    [Fact]
    public async Task Cancel_LinkedPurchaseInvoice_ClearsReceivedDfeLink()
    {
        var (db, _) = Services();
        var invoice = await SeedLinkedInvoiceAsync(db);

        await FinancialDocumentTestServices.PurchaseCancel(db).ExecuteAsync(invoice.Key, "ana");

        Assert.Null((await db.Context.ReceivedDfes.AsNoTracking().SingleAsync()).PurchaseInvoiceDocEntry);
    }
}
