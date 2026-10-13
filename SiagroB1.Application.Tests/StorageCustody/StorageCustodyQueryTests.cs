using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra.Context;
using SiagroB1.Infra.Queries;

namespace SiagroB1.Application.Tests.StorageCustody;

public class StorageCustodyQueryTests
{
    [Fact]
    public async Task Transactions_carry_the_lot_and_the_transfer_status()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageCustodySeed.CreateAsync(db);
        var plain = await seed.AddTransactionAsync();
        var transfer = await seed.AddTransferAsync(OwnershipTransferStatus.Cancelled, "L0002");
        var fromTransfer = await seed.AddTransactionAsync(StorageTransactionType.Shipment, ownershipTransferKey: transfer.Key);

        var rows = await StorageCustodyQuery.Transactions(db.Context).ToListAsync();

        var row = rows.Single(r => r.Key == plain.Key);
        Assert.Equal(StorageCustodySeed.Depositor, row.CardCode);
        Assert.Equal("L0001", row.StorageAddressCode);
        Assert.Equal(StorageCustodySeed.Branch, row.BranchCode);
        Assert.True(row.RequiresProcessing);
        Assert.False(row.IsOwnershipTransfer);
        var transferRow = rows.Single(r => r.Key == fromTransfer.Key);
        Assert.True(transferRow.IsOwnershipTransfer);
        Assert.True(transferRow.TransferCancelled);
    }

    [Fact]
    public async Task Own_lot_romaneio_is_not_a_custody_transaction()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageCustodySeed.CreateAsync(db);
        seed.Lot.OwnershipType = StorageOwnershipType.OwnedInOurCustody;
        await db.SaveChangesAsync();
        await seed.AddTransactionAsync();

        Assert.Empty(await StorageCustodyQuery.Transactions(db.Context).ToListAsync());
    }

    [Fact]
    public async Task Active_documents_skip_cancelled_and_sum_the_lines()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageCustodySeed.CreateAsync(db);
        var t = await seed.AddTransactionAsync();
        await seed.AddPurchaseDocumentAsync(t, StorageCustodyRole.ProcessingEntry, 30_000m, status: InvoiceStatus.Cancelled);
        var live = await seed.AddPurchaseDocumentAsync(t, StorageCustodyRole.ProcessingEntry, 30_000m);
        await seed.AddSalesDocumentAsync(t, StorageCustodyRole.ProcessingSymbolicReturn, 30_000m);

        var docs = await StorageCustodyQuery.ActiveDocumentsAsync(db.Context, [t.Key]);

        Assert.Equal(2, docs.Count);
        var entry = docs.Single(d => !d.IsSalesInvoice);
        Assert.Equal(live.Key, entry.DocumentKey);
        Assert.Equal(30_000m, entry.Quantity);
        Assert.Equal("1901", entry.Cfop);
        Assert.Equal(DocumentIssuerType.Own, docs.Single(d => d.IsSalesInvoice).IssuerType);
    }

    [Fact]
    public void Queries_translate_to_sql_server()
    {
        using var ctx = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer("Server=model-inspection-only;Database=none").Options);

        Assert.NotEmpty(StorageCustodyQuery.Transactions(ctx).ToQueryString());
        // Composição sobre a projeção (guarda, resumo e relatório filtram depois do Select).
        var key = Guid.NewGuid();
        var until = new DateTime(2026, 11, 1);
        Assert.NotEmpty(StorageCustodyQuery.Transactions(ctx)
            .Where(t => t.Key == key || (t.StorageAddressCode == "L1" && t.TransactionDate < until)).ToQueryString());
        Assert.NotEmpty(StorageCustodyQuery.PurchaseDocuments(ctx, [Guid.NewGuid()]).ToQueryString());
        Assert.NotEmpty(StorageCustodyQuery.SalesDocuments(ctx, [Guid.NewGuid()]).ToQueryString());
    }
}
