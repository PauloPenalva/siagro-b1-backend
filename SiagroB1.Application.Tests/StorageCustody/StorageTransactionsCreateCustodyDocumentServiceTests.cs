using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services;
using SiagroB1.Application.Services.PurchaseInvoices;
using SiagroB1.Application.Services.SalesInvoices;
using SiagroB1.Application.Services.StorageCustody;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.StorageCustody;

public class StorageTransactionsCreateCustodyDocumentServiceTests
{
    private static StorageTransactionsCreateCustodyDocumentService Service(UnitOfWork db, string erp = "STANDALONE")
    {
        var partners = new FakeBusinessPartnerService(
            names: new Dictionary<string, string> { [StorageCustodySeed.Depositor] = "PRODUTOR UM" },
            states: new Dictionary<string, string> { [StorageCustodySeed.Depositor] = "SP" });
        var usages = new UsageService(db, NullLogger<UsageService>.Instance);
        var sales = new SalesInvoicesCreateService(
            db, partners, new FakeItemService(), new FakeDocNumberSequenceService(),
            new SalesInvoicesUsageGuardService(usages), new SalesInvoicesCfopResolveService(db, usages, partners),
            TaxTestServices.InactiveApply(db), TaxTestServices.InactiveFiscalComplement(db), NullLogger<SalesInvoicesCreateService>.Instance);
        var purchases = new PurchaseInvoicesCreateService(
            db, partners, new FakeItemService(), TaxTestServices.InactivePurchaseApply(db), new FakeDocNumberSequenceService());
        return new(db, TestConfiguration.Erp(erp), partners, usages, purchases, sales,
            NullLogger<StorageTransactionsCreateCustodyDocumentService>.Instance);
    }

    private static async Task<(UnitOfWork Db, StorageCustodySeed Seed)> SeedAsync(bool processing = true)
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageCustodySeed.CreateAsync(db, processing);
        // A natureza precisa de efeito configurado para o documento de saída (SalesInvoicesUsageGuardService).
        return (db, seed);
    }

    [Fact]
    public async Task Quantities_follow_gross_gross_net()
    {
        var (db, seed) = await SeedAsync();
        var t = await seed.AddTransactionAsync(gross: 30_000m, net: 28_000m);

        var entryKey = await Service(db).ExecuteAsync(new(t.Key, StorageCustodyRole.ProcessingEntry, StorageCustodySeed.ProcessingEntryUsage, 2.5m), "tester");

        var entry = await db.Context.PurchaseInvoices.Include(i => i.Items).SingleAsync(i => i.Key == entryKey);
        Assert.Equal(DocumentIssuerType.Own, entry.IssuerType);
        Assert.Equal(InvoiceStatus.Pending, entry.InvoiceStatus);
        Assert.Equal(StorageCustodySeed.Depositor, entry.CardCode);
        Assert.Equal(t.Key, entry.CustodyStorageTransactionKey);
        Assert.Equal(StorageCustodyRole.ProcessingEntry, entry.CustodyRole);
        Assert.Equal(30_000m, Assert.Single(entry.Items).Quantity);
        Assert.Equal(2.5m, entry.Items.Single().UnitPrice);
        Assert.Equal(30_000m, entry.GrossWeight);
        Assert.Equal("ABC1D23", entry.TruckCode);

        // A entrada é "autorizada" para liberar a devolução simbólica.
        entry.NfeStatus = NfeStatus.Authorized;
        entry.InvoiceStatus = InvoiceStatus.Confirmed;
        entry.ChaveNFe = StorageCustodySeed.AccessKey;
        await db.SaveChangesAsync();

        var symbolicKey = await Service(db).ExecuteAsync(new(t.Key, StorageCustodyRole.ProcessingSymbolicReturn, StorageCustodySeed.SymbolicReturnUsage, 2.5m), "tester");
        var symbolic = await db.Context.SalesInvoices.Include(i => i.Items).SingleAsync(i => i.Key == symbolicKey);
        Assert.Equal(30_000m, Assert.Single(symbolic.Items).Quantity);
        Assert.Equal(StorageCustodyRole.ProcessingSymbolicReturn, symbolic.CustodyRole);

        var storageKey = await Service(db).ExecuteAsync(new(t.Key, StorageCustodyRole.StorageEntry, StorageCustodySeed.StorageEntryUsage, 2.5m), "tester");
        Assert.Equal(28_000m, (await db.Context.PurchaseInvoicesItems.SingleAsync(l => l.PurchaseInvoiceKey == storageKey)).Quantity);
    }

    [Fact]
    public async Task Symbolic_return_takes_the_quantity_of_the_producer_invoice()
    {
        var (db, seed) = await SeedAsync();
        var t = await seed.AddTransactionAsync(gross: 30_000m);
        await seed.AddPurchaseDocumentAsync(t, StorageCustodyRole.ProcessingEntry, 29_500m, DocumentIssuerType.ThirdParty,
            InvoiceStatus.Confirmed, accessKey: StorageCustodySeed.AccessKey);

        var key = await Service(db).ExecuteAsync(new(t.Key, StorageCustodyRole.ProcessingSymbolicReturn, StorageCustodySeed.SymbolicReturnUsage, 2m), "tester");

        Assert.Equal(29_500m, (await db.Context.SalesInvoicesItems.SingleAsync(l => l.SalesInvoiceKey == key)).Quantity);
    }

    [Fact]
    public async Task Shipment_generates_the_return_by_the_net_weight()
    {
        var (db, seed) = await SeedAsync();
        var t = await seed.AddTransactionAsync(StorageTransactionType.Shipment, gross: 15_200m, net: 15_000m);

        var key = await Service(db).ExecuteAsync(new(t.Key, StorageCustodyRole.StorageReturn, StorageCustodySeed.StorageReturnUsage, 2m), "tester");

        Assert.Equal(15_000m, (await db.Context.SalesInvoicesItems.SingleAsync(l => l.SalesInvoiceKey == key)).Quantity);
    }

    [Fact]
    public async Task Refuses_in_sap_mode()
    {
        var (db, seed) = await SeedAsync();
        var t = await seed.AddTransactionAsync();

        var e = await Assert.ThrowsAsync<DefaultException>(() =>
            Service(db, "SAPB1").ExecuteAsync(new(t.Key, StorageCustodyRole.ProcessingEntry, StorageCustodySeed.ProcessingEntryUsage, 2m), "tester"));

        Assert.Equal("Disponível somente no modo STANDALONE.", e.Message);
    }

    [Fact]
    public async Task Refuses_usage_of_another_role_and_non_positive_price()
    {
        var (db, seed) = await SeedAsync();
        var t = await seed.AddTransactionAsync();

        var usage = await Assert.ThrowsAsync<DefaultException>(() =>
            Service(db).ExecuteAsync(new(t.Key, StorageCustodyRole.ProcessingEntry, StorageCustodySeed.StorageEntryUsage, 2m), "tester"));
        Assert.Equal("Escolha uma natureza de entrada para beneficiamento.", usage.Message);

        var price = await Assert.ThrowsAsync<DefaultException>(() =>
            Service(db).ExecuteAsync(new(t.Key, StorageCustodyRole.ProcessingEntry, StorageCustodySeed.ProcessingEntryUsage, 0m), "tester"));
        Assert.Equal("Informe o preço unitário da nota.", price.Message);
    }

    [Fact]
    public async Task Symbolic_return_without_processing_entry_is_refused()
    {
        var (db, seed) = await SeedAsync();
        var t = await seed.AddTransactionAsync();

        var e = await Assert.ThrowsAsync<DefaultException>(() =>
            Service(db).ExecuteAsync(new(t.Key, StorageCustodyRole.ProcessingSymbolicReturn, StorageCustodySeed.SymbolicReturnUsage, 2m), "tester"));

        Assert.Equal("Gere antes a entrada para beneficiamento deste romaneio.", e.Message);
    }

    [Fact]
    public async Task Regenerates_after_the_previous_was_cancelled()
    {
        var (db, seed) = await SeedAsync(processing: false);
        var t = await seed.AddTransactionAsync();
        var first = await seed.AddPurchaseDocumentAsync(t, StorageCustodyRole.StorageEntry, 28_000m);

        await Assert.ThrowsAsync<DefaultException>(() =>
            Service(db).ExecuteAsync(new(t.Key, StorageCustodyRole.StorageEntry, StorageCustodySeed.StorageEntryUsage, 2m), "tester"));

        first.InvoiceStatus = InvoiceStatus.Cancelled;
        await db.SaveChangesAsync();
        await Service(db).ExecuteAsync(new(t.Key, StorageCustodyRole.StorageEntry, StorageCustodySeed.StorageEntryUsage, 2m), "tester");
    }
}
