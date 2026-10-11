using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.PurchaseInvoices;
using SiagroB1.Application.Services.StorageDeposits;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Application.Tests.StorageDeposits;

public class StorageDepositReturnGuardTests
{
    private static PurchaseInvoiceItem Line(StorageDepositSeed seed, Guid? remittanceItemKey, decimal qty, string item = StorageDepositSeed.Item) => new()
    {
        Key = Guid.NewGuid(), ItemCode = item, UnitOfMeasureCode = "KG", Quantity = qty, UnitPrice = 1m,
        UsageCode = seed.ReturnUsageCode, SalesInvoiceItemKey = remittanceItemKey,
    };

    [Fact]
    public async Task Return_line_requires_the_remittance()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageDepositSeed.CreateAsync(db);

        var e = await Assert.ThrowsAsync<DefaultException>(() =>
            StorageDepositReturnGuard.EnsureAsync(db.Context, StorageDepositSeed.Warehouse, null, [Line(seed, null, 1m)]));

        Assert.Equal("Linha SOJA com natureza de retorno de depósito precisa indicar a remessa.", e.Message);
    }

    [Fact]
    public async Task Linked_document_must_be_an_active_remittance()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageDepositSeed.CreateAsync(db);
        var remittance = await seed.AddRemittanceAsync(100_000m, InvoiceStatus.Cancelled);

        var e = await Assert.ThrowsAsync<DefaultException>(() =>
            StorageDepositReturnGuard.EnsureAsync(db.Context, StorageDepositSeed.Warehouse, null,
                [Line(seed, remittance.Items.Single().Key, 1m)]));

        Assert.Equal("A linha SOJA aponta um documento que não é remessa para depósito ativa.", e.Message);
    }

    [Fact]
    public async Task Return_must_be_of_the_same_item()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageDepositSeed.CreateAsync(db);
        var remittance = await seed.AddRemittanceAsync(100_000m);

        var e = await Assert.ThrowsAsync<DefaultException>(() =>
            StorageDepositReturnGuard.EnsureAsync(db.Context, StorageDepositSeed.Warehouse, null,
                [Line(seed, remittance.Items.Single().Key, 1m, item: "MILHO")]));

        Assert.Equal("A linha MILHO é de outro produto que a remessa nº 000000123 (SOJA).", e.Message);
    }

    [Fact]
    public async Task Return_must_come_from_the_warehouse()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageDepositSeed.CreateAsync(db);
        var remittance = await seed.AddRemittanceAsync(100_000m);

        var e = await Assert.ThrowsAsync<DefaultException>(() =>
            StorageDepositReturnGuard.EnsureAsync(db.Context, "OUTRO", null, [Line(seed, remittance.Items.Single().Key, 1m)]));

        Assert.Equal("O retorno precisa ser do armazém ARM001, destinatário da remessa nº 000000123.", e.Message);
    }

    [Fact]
    public async Task Quantity_cannot_pass_the_pending_balance_counting_other_documents()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageDepositSeed.CreateAsync(db);
        var remittance = await seed.AddRemittanceAsync(100_000m);
        await seed.AddReturnAsync(remittance, 70_000m);

        var e = await Assert.ThrowsAsync<DefaultException>(() =>
            StorageDepositReturnGuard.EnsureAsync(db.Context, StorageDepositSeed.Warehouse, null,
                [Line(seed, remittance.Items.Single().Key, 30_001m)]));

        Assert.Equal("Retorno de depósito: a quantidade (30.001,000) passa do saldo a retornar da remessa nº 000000123 (30.000,000).", e.Message);
    }

    [Fact]
    public async Task Two_lines_of_the_same_document_add_up_against_the_balance()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageDepositSeed.CreateAsync(db);
        var remittance = await seed.AddRemittanceAsync(100_000m);
        var key = remittance.Items.Single().Key;

        await Assert.ThrowsAsync<DefaultException>(() =>
            StorageDepositReturnGuard.EnsureAsync(db.Context, StorageDepositSeed.Warehouse, null,
                [Line(seed, key, 60_000m), Line(seed, key, 40_001m)]));
    }

    [Fact]
    public async Task Editing_a_line_does_not_count_its_old_quantity()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageDepositSeed.CreateAsync(db);
        var remittance = await seed.AddRemittanceAsync(100_000m);
        var mine = await seed.AddReturnAsync(remittance, 90_000m);
        var edited = mine.Items.Single();
        edited.Quantity = 100_000m;

        await StorageDepositReturnGuard.EnsureAsync(db.Context, StorageDepositSeed.Warehouse, mine.Key, [edited]);
    }

    [Fact]
    public async Task Cancelled_return_gives_the_balance_back()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageDepositSeed.CreateAsync(db);
        var remittance = await seed.AddRemittanceAsync(100_000m);
        await seed.AddReturnAsync(remittance, 100_000m, InvoiceStatus.Cancelled);

        await StorageDepositReturnGuard.EnsureAsync(db.Context, StorageDepositSeed.Warehouse, null,
            [Line(seed, remittance.Items.Single().Key, 100_000m)]);
    }

    [Fact]
    public async Task Remittance_usage_is_refused_on_incoming_document()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageDepositSeed.CreateAsync(db);
        var line = Line(seed, null, 1m);
        line.UsageCode = seed.RemittanceUsageCode;

        var e = await Assert.ThrowsAsync<DefaultException>(() =>
            StorageDepositReturnGuard.EnsureAsync(db.Context, StorageDepositSeed.Warehouse, null, [line]));

        Assert.Equal("Natureza de remessa para depósito não pode ser usada no documento de entrada.", e.Message);
    }

    [Fact]
    public async Task Lines_without_deposit_role_pass()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageDepositSeed.CreateAsync(db);
        var line = Line(seed, null, 1m);
        line.UsageCode = null;

        await StorageDepositReturnGuard.EnsureAsync(db.Context, StorageDepositSeed.Warehouse, null, [line]);
    }

    // --- um teste por caminho de gravação: o guard está plantado ---

    private const string RequiresRemittance = "Linha SOJA com natureza de retorno de depósito precisa indicar a remessa.";

    private static async Task<(SiagroB1.Infra.UnitOfWork Db, StorageDepositSeed Seed, PurchaseInvoice Invoice)> PendingDocumentAsync()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageDepositSeed.CreateAsync(db);
        var invoice = new PurchaseInvoice
        {
            Key = Guid.NewGuid(), BranchCode = StorageDepositSeed.Branch, CardCode = StorageDepositSeed.Warehouse,
            InvoiceNumber = "E000001", InvoiceStatus = InvoiceStatus.Pending,
            Items = [new PurchaseInvoiceItem { Key = Guid.NewGuid(), ItemCode = StorageDepositSeed.Item, UnitOfMeasureCode = "KG", Quantity = 1m, UnitPrice = 1m }],
        };
        db.Context.PurchaseInvoices.Add(invoice);
        await db.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();
        return (db, seed, invoice);
    }

    [Fact]
    public async Task Create_path_runs_the_guard()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageDepositSeed.CreateAsync(db);
        var invoice = new PurchaseInvoice
        {
            Key = Guid.NewGuid(), BranchCode = StorageDepositSeed.Branch, CardCode = StorageDepositSeed.Warehouse,
            Items = [Line(seed, null, 1m)],
        };

        var e = await Assert.ThrowsAsync<DefaultException>(() =>
            new PurchaseInvoicesCreateService(db, new FakeBusinessPartnerService(), new FakeItemService(),
                TaxTestServices.InactivePurchaseApply(db), new FakeDocNumberSequenceService()).ExecuteAsync(invoice, "tester"));

        Assert.Equal(RequiresRemittance, e.Message);
    }

    [Fact]
    public async Task Header_sync_path_runs_the_guard()
    {
        var (db, seed, saved) = await PendingDocumentAsync();
        var incoming = new PurchaseInvoice { CardCode = StorageDepositSeed.Warehouse, Items = [Line(seed, null, 1m)] };

        var e = await Assert.ThrowsAsync<DefaultException>(() =>
            new PurchaseInvoicesUpdateService(db, new FakeBusinessPartnerService(), new FakeItemService(),
                TaxTestServices.InactivePurchaseApply(db)).ExecuteAsync(saved.Key, incoming, "tester"));

        Assert.Equal(RequiresRemittance, e.Message);
    }

    [Fact]
    public async Task Line_create_path_runs_the_guard()
    {
        var (db, seed, saved) = await PendingDocumentAsync();
        var line = Line(seed, null, 1m);
        line.PurchaseInvoiceKey = saved.Key;

        var e = await Assert.ThrowsAsync<DefaultException>(() =>
            new PurchaseInvoicesItemsCreateService(db, new FakeItemService(), TaxTestServices.InactivePurchaseApply(db))
                .ExecuteAsync(line, "tester"));

        Assert.Equal(RequiresRemittance, e.Message);
    }

    [Fact]
    public async Task Line_update_path_runs_the_guard()
    {
        var (db, seed, saved) = await PendingDocumentAsync();
        var key = saved.Items.Single().Key!.Value;
        var patch = Line(seed, null, 1m);
        patch.Key = key;

        var e = await Assert.ThrowsAsync<DefaultException>(() =>
            new PurchaseInvoicesItemsUpdateService(db, new FakeItemService(), TaxTestServices.InactivePurchaseApply(db))
                .ExecuteAsync(key, patch, "tester"));

        Assert.Equal(RequiresRemittance, e.Message);
    }
}
