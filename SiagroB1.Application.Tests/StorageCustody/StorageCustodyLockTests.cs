using SiagroB1.Application.Services.PurchaseInvoices;
using SiagroB1.Application.Services.ShipmentReleases;
using SiagroB1.Application.Services.StorageAddresses;
using SiagroB1.Application.Services.StorageCustody;
using SiagroB1.Application.Services.StorageTransactions;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Application.Tests.StorageCustody;

/// <summary>A cadeia se desfaz na ordem inversa; romaneio e transferência com nota ativa não são estornados (spec §4.5).</summary>
public class StorageCustodyLockTests
{
    [Fact]
    public async Task Processing_entry_is_not_cancelled_with_the_symbolic_return_active()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageCustodySeed.CreateAsync(db);
        var t = await seed.AddTransactionAsync();
        var entry = await seed.AddPurchaseDocumentAsync(t, StorageCustodyRole.ProcessingEntry, 30_000m, DocumentIssuerType.ThirdParty, InvoiceStatus.Confirmed);
        var symbolic = await seed.AddSalesDocumentAsync(t, StorageCustodyRole.ProcessingSymbolicReturn, 30_000m, number: "000000077");

        var e = await Assert.ThrowsAsync<DefaultException>(() => new PurchaseInvoicesCancelService(db).ExecuteAsync(entry.Key, "tester"));
        Assert.Equal("Cancele primeiro a nota de devolução simbólica do beneficiamento nº 000000077 deste romaneio.", e.Message);

        symbolic.InvoiceStatus = InvoiceStatus.Cancelled;
        await db.SaveChangesAsync();
        await new PurchaseInvoicesCancelService(db).ExecuteAsync(entry.Key, "tester");
    }

    [Fact]
    public async Task Symbolic_return_is_not_cancelled_with_the_storage_entry_active()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageCustodySeed.CreateAsync(db);
        var t = await seed.AddTransactionAsync();
        var symbolic = await seed.AddSalesDocumentAsync(t, StorageCustodyRole.ProcessingSymbolicReturn, 30_000m);
        await seed.AddPurchaseDocumentAsync(t, StorageCustodyRole.StorageEntry, 28_000m, number: "000000088");

        var e = await Assert.ThrowsAsync<DefaultException>(() =>
            StorageCustodyGuard.EnsureSalesHasNoLaterStepAsync(db.Context, symbolic.Key));

        Assert.Equal("Cancele primeiro a nota de entrada para armazenagem nº 000000088 deste romaneio.", e.Message);
    }

    [Fact]
    public async Task Pending_processing_entry_is_not_deleted_with_the_symbolic_return_active()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageCustodySeed.CreateAsync(db);
        var t = await seed.AddTransactionAsync();
        var entry = await seed.AddPurchaseDocumentAsync(t, StorageCustodyRole.ProcessingEntry, 30_000m, DocumentIssuerType.ThirdParty);
        await seed.AddSalesDocumentAsync(t, StorageCustodyRole.ProcessingSymbolicReturn, 30_000m);

        await Assert.ThrowsAsync<DefaultException>(() => new PurchaseInvoicesDeleteService(db).ExecuteAsync(entry.Key));
    }

    [Fact]
    public async Task Romaneio_with_active_custody_document_is_not_cancelled_or_reversed()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageCustodySeed.CreateAsync(db);
        var t = await seed.AddTransactionAsync();
        t.TransactionOrigin = TransactionCode.StorageTransaction;
        await db.SaveChangesAsync();
        var doc = await seed.AddPurchaseDocumentAsync(t, StorageCustodyRole.ProcessingEntry, 30_000m);
        var recalc = new ShipmentReleasesRecalculateShippedService(db.Context);

        var cancel = await Assert.ThrowsAsync<ApplicationException>(() =>
            new StorageTransactionsCancelService(db, recalc).ExecuteAsync(t.Key, "tester"));
        Assert.StartsWith("O romaneio tem nota fiscal de custódia ativa", cancel.Message);

        var reverse = await Assert.ThrowsAsync<ApplicationException>(() =>
            new StorageTransactionsReverseService(db, new StorageAddressesGetBalanceService(null!), recalc, new FakeStringLocalizer<Resource>())
                .ExecuteAsync(t.Key, "tester"));
        Assert.Equal("O romaneio tem nota fiscal de custódia ativa (nº 000000101). Cancele as notas fiscais de custódia antes de estornar.", reverse.Message);

        doc.InvoiceStatus = InvoiceStatus.Cancelled;
        await db.SaveChangesAsync();
        await new StorageTransactionsCancelService(db, recalc).ExecuteAsync(t.Key, "tester");
    }

    [Fact]
    public async Task Requires_processing_is_locked_while_the_lot_has_custody_documents()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageCustodySeed.CreateAsync(db);
        var t = await seed.AddTransactionAsync();
        await seed.AddPurchaseDocumentAsync(t, StorageCustodyRole.ProcessingEntry, 30_000m);
        var service = new StorageAddressesUpdateService(
            db.Context, new FakeBusinessPartnerService(), new FakeItemService(), new FakeWarehouseService(),
            new TestLogger<StorageAddressesUpdateService>());

        seed.Lot.RequiresProcessing = false; // PATCH: a instância rastreada chega já mutada
        var e = await Assert.ThrowsAsync<ApplicationException>(() => service.ExecuteAsync(seed.Lot.Code!, seed.Lot, "tester"));

        Assert.Equal("O lote já possui notas fiscais de custódia; não é possível alterar depositante, produto, filial, tipo de saldo ou o beneficiamento.", e.Message);
    }

    [Theory]
    [InlineData("CardCode")]
    [InlineData("ItemCode")]
    [InlineData("BranchCode")]
    [InlineData("OwnershipType")]
    public async Task Lot_identity_is_locked_while_the_lot_has_custody_documents(string field)
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageCustodySeed.CreateAsync(db);
        var t = await seed.AddTransactionAsync();
        await seed.AddPurchaseDocumentAsync(t, StorageCustodyRole.ProcessingEntry, 30_000m);
        var service = new StorageAddressesUpdateService(
            db.Context, new FakeBusinessPartnerService(), new FakeItemService(), new FakeWarehouseService(),
            new TestLogger<StorageAddressesUpdateService>());

        MutateIdentity(seed.Lot, field);
        var e = await Assert.ThrowsAsync<ApplicationException>(() => service.ExecuteAsync(seed.Lot.Code!, seed.Lot, "tester"));

        Assert.Equal("O lote já possui notas fiscais de custódia; não é possível alterar depositante, produto, filial, tipo de saldo ou o beneficiamento.", e.Message);
    }

    [Theory]
    [InlineData("CardCode")]
    [InlineData("ItemCode")]
    [InlineData("BranchCode")]
    [InlineData("OwnershipType")]
    public async Task Lot_identity_changes_freely_without_documents(string field)
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageCustodySeed.CreateAsync(db);
        await seed.AddTransactionAsync();
        var service = new StorageAddressesUpdateService(
            db.Context, new FakeBusinessPartnerService(), new FakeItemService(), new FakeWarehouseService(),
            new TestLogger<StorageAddressesUpdateService>());

        MutateIdentity(seed.Lot, field);
        await service.ExecuteAsync(seed.Lot.Code!, seed.Lot, "tester");
    }

    private static void MutateIdentity(StorageAddress lot, string field)
    {
        switch (field)
        {
            case "CardCode": lot.CardCode = "P0002"; break;
            case "ItemCode": lot.ItemCode = "MILHO"; break;
            case "BranchCode": lot.BranchCode = "02"; break;
            default: lot.OwnershipType = StorageOwnershipType.OwnedInOurCustody; break;
        }
    }

    [Fact]
    public async Task Requires_processing_changes_freely_without_documents()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageCustodySeed.CreateAsync(db);
        await seed.AddTransactionAsync();
        var service = new StorageAddressesUpdateService(
            db.Context, new FakeBusinessPartnerService(), new FakeItemService(), new FakeWarehouseService(),
            new TestLogger<StorageAddressesUpdateService>());

        seed.Lot.RequiresProcessing = false;
        await service.ExecuteAsync(seed.Lot.Code!, seed.Lot, "tester");
    }
}
