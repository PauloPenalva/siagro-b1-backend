using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using SiagroB1.Infra.Queries;
using Microsoft.EntityFrameworkCore;

namespace SiagroB1.Application.Tests.Royalties;

public class RoyaltyBalanceQueryTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private StorageTransaction Receipt(
        string code, decimal net, RoyaltySituation? situation = RoyaltySituation.Declared,
        StorageTransactionsStatus status = StorageTransactionsStatus.Confirmed,
        StorageTransactionType type = StorageTransactionType.Receipt, string item = "SOJA")
    {
        var st = new StorageTransaction
        {
            Key = Guid.NewGuid(), Code = code, CardCode = "P0001", ItemCode = item, UnitOfMeasureCode = "KG",
            WarehouseCode = "ARM01", TransactionType = type, TransactionStatus = status,
            TransactionDate = new DateTime(2026, 10, 1), NetWeight = net, RoyaltySituation = situation,
            RoyaltyOriginCode = "P0001",
        };
        _db.Context.StorageTransactions.Add(st);
        return st;
    }

    private void WriteOff(StorageTransaction st, decimal weight, RoyaltyWriteOffStatus status = RoyaltyWriteOffStatus.Active)
    {
        var w = new RoyaltyWriteOff
        {
            ItsId = Guid.NewGuid().ToString("N")[..10], TaxId = "12345678909", Name = "X",
            Situation = st.RoyaltySituation!.Value, Status = status, WriteOffDate = DateTime.Today,
        };
        w.Lines.Add(new RoyaltyWriteOffLine { StorageTransactionKey = st.Key, Weight = weight });
        _db.Context.RoyaltyWriteOffs.Add(w);
    }

    private async Task<List<RoyaltyBalanceRow>> RunAsync(RoyaltyBalanceFilter? filter = null) =>
        await RoyaltyBalanceQuery.Apply(RoyaltyBalanceQuery.Build(_db.Context), filter ?? new RoyaltyBalanceFilter())
            .OrderBy(x => x.Code).ToListAsync();

    public RoyaltyBalanceQueryTests()
    {
        _db.Context.ItemComplements.Add(new ItemComplement { ItemCode = "SOJA", ControlsRoyalties = true });
        _db.Context.ItemComplements.Add(new ItemComplement { ItemCode = "MILHO", ControlsRoyalties = false });
    }

    [Fact]
    public async Task Balance_is_net_weight_minus_active_write_offs()
    {
        var st = Receipt("R1", 30_000m);
        WriteOff(st, 10_000m);
        WriteOff(st, 5_000m, RoyaltyWriteOffStatus.Cancelled);
        await _db.SaveChangesAsync();

        var row = Assert.Single(await RunAsync());

        Assert.Equal(10_000m, row.WrittenOffWeight);
        Assert.Equal(20_000m, row.Balance);
    }

    [Fact]
    public async Task Only_confirmed_or_invoiced_receipts_of_controlled_items_with_situation_are_eligible()
    {
        Receipt("A-ok", 1m);
        Receipt("B-invoiced", 1m, status: StorageTransactionsStatus.Invoiced);
        Receipt("C-pending", 1m, status: StorageTransactionsStatus.Pending);
        Receipt("D-cancelled", 1m, status: StorageTransactionsStatus.Cancelled);
        Receipt("E-legacy", 1m, situation: null);
        Receipt("F-transshipment", 1m, type: StorageTransactionType.TransshipmentReceipt);
        Receipt("G-purchase", 1m, type: StorageTransactionType.Purchase);
        Receipt("H-corn", 1m, item: "MILHO");
        Receipt("I-sales", 1m, type: StorageTransactionType.SalesShipment);
        Receipt("J-reconciliation", 1m, type: StorageTransactionType.Purchase).TransactionOrigin =
            TransactionCode.WarehouseReconciliation;
        await _db.SaveChangesAsync();

        var rows = await RunAsync();

        Assert.Equal(["A-ok", "B-invoiced", "G-purchase"], rows.Select(x => x.Code));
        Assert.Equal(StorageTransactionType.Purchase, rows.Single(x => x.Code == "G-purchase").TransactionType);
    }

    [Fact]
    public async Task Storage_entry_counts_once_through_the_purchase()
    {
        var purchase = Receipt("C1", 1_000m, type: StorageTransactionType.Purchase);
        var receipt = Receipt("R1", 1_000m);
        _db.Context.StorageEntryTransactions.Add(new StorageEntryTransaction
        {
            Key = Guid.NewGuid(), PurchaseStorageTransactionKey = purchase.Key,
            ReceiptStorageTransactionKey = receipt.Key, PurchaseContractKey = Guid.NewGuid(),
            Status = StorageEntryTransactionStatus.Confirmed,
        });
        await _db.SaveChangesAsync();

        Assert.Equal(["C1"], (await RunAsync()).Select(x => x.Code));
    }

    [Fact]
    public async Task Release_change_counts_only_the_new_purchase()
    {
        var original = Receipt("C-old", 1_000m, type: StorageTransactionType.Purchase);
        var replacement = Receipt("C-new", 1_000m, type: StorageTransactionType.Purchase);
        _db.Context.ShippingReleaseChanges.Add(new ShippingReleaseChange
        {
            Key = Guid.NewGuid(),
            Reason = "troca de liberação",
            OriginalPurchaseStorageTransactionKey = original.Key,
            NewPurchaseStorageTransactionKey = replacement.Key,
        });
        await _db.SaveChangesAsync();

        Assert.Equal(["C-new"], (await RunAsync()).Select(x => x.Code));
    }

    private void SupplierChange(StorageTransaction original, StorageTransaction replacement, decimal quantity) =>
        _db.Context.PurchaseTransactionChanges.Add(new PurchaseTransactionChange
        {
            Key = Guid.NewGuid(), OriginalStorageTransactionKey = original.Key,
            ReturnStorageTransactionKey = Guid.NewGuid(), NewStorageTransactionKey = replacement.Key,
            PurchaseContractKey = Guid.NewGuid(), OriginalCardCode = "P0001", OriginalWarehouseCode = "ARM01",
            NewCardCode = "P0002", NewWarehouseCode = "ARM02", Quantity = quantity, AllocatedVolume = quantity,
            Reason = "fornecedor errado",
        });

    [Fact]
    public async Task Full_supplier_change_counts_only_the_new_purchase()
    {
        var original = Receipt("C-old", 1_000m, type: StorageTransactionType.Purchase);
        var replacement = Receipt("C-new", 1_000m, type: StorageTransactionType.Purchase);
        SupplierChange(original, replacement, 1_000m);
        await _db.SaveChangesAsync();

        Assert.Equal(["C-new"], (await RunAsync(new RoyaltyBalanceFilter { Scope = RoyaltyBalanceScope.All }))
            .Select(x => x.Code));
    }

    [Fact]
    public async Task Partial_supplier_change_leaves_only_the_unchanged_part_on_the_original()
    {
        var original = Receipt("C-old", 1_000m, type: StorageTransactionType.Purchase);
        var first = Receipt("C-new1", 300m, type: StorageTransactionType.Purchase);
        var second = Receipt("C-new2", 100m, type: StorageTransactionType.Purchase);
        SupplierChange(original, first, 300m);
        SupplierChange(original, second, 100m);
        WriteOff(original, 50m);
        await _db.SaveChangesAsync();

        var rows = await RunAsync();

        Assert.Equal(["C-new1", "C-new2", "C-old"], rows.Select(x => x.Code));
        var old = rows.Single(x => x.Code == "C-old");
        Assert.Equal(600m, old.NetWeight);
        Assert.Equal(550m, old.Balance);
        Assert.Equal(950m, rows.Sum(x => x.Balance));
    }

    [Fact]
    public async Task Pending_scope_hides_fully_written_off_receipts()
    {
        var st = Receipt("R1", 1_234.567m);
        WriteOff(st, 1_234.567m);
        await _db.SaveChangesAsync();

        Assert.Empty(await RunAsync());
        Assert.Empty(await RunAsync(new RoyaltyBalanceFilter { Scope = RoyaltyBalanceScope.Pending }));
    }

    [Fact]
    public async Task Written_off_scope_keeps_only_fully_written_off_receipts()
    {
        var full = Receipt("R1", 1_000m);
        WriteOff(full, 1_000m);
        var partial = Receipt("R2", 1_000m);
        WriteOff(partial, 400m);
        Receipt("R3", 1_000m);
        var cancelled = Receipt("R4", 1_000m);
        WriteOff(cancelled, 1_000m, RoyaltyWriteOffStatus.Cancelled);
        Receipt("R5", 0m);
        await _db.SaveChangesAsync();

        var rows = await RunAsync(new RoyaltyBalanceFilter { Scope = RoyaltyBalanceScope.WrittenOff });

        Assert.Equal(["R1"], rows.Select(x => x.Code));
    }

    [Fact]
    public async Task All_scope_has_no_balance_filter()
    {
        var full = Receipt("R1", 1_000m);
        WriteOff(full, 1_000m);
        Receipt("R2", 1_000m);
        await _db.SaveChangesAsync();

        var rows = await RunAsync(new RoyaltyBalanceFilter { Scope = RoyaltyBalanceScope.All });

        Assert.Equal(["R1", "R2"], rows.Select(x => x.Code));
    }

    [Fact]
    public async Task Filters_by_situation_and_period()
    {
        Receipt("R1", 1m, RoyaltySituation.Declared);
        var tested = Receipt("R2", 1m, RoyaltySituation.Tested);
        tested.TransactionDate = new DateTime(2026, 10, 5);
        await _db.SaveChangesAsync();

        var rows = await RunAsync(new RoyaltyBalanceFilter
        {
            Situation = RoyaltySituation.Tested, FromDate = new DateTime(2026, 10, 5), ToDate = new DateTime(2026, 10, 5),
        });

        Assert.Equal(["R2"], rows.Select(x => x.Code));
    }

    [Fact]
    public async Task Totals_group_by_situation()
    {
        Receipt("R1", 100m, RoyaltySituation.Declared);
        Receipt("R2", 50m, RoyaltySituation.Declared);
        var conv = Receipt("R3", 40m, RoyaltySituation.Conventional);
        WriteOff(conv, 10m);
        await _db.SaveChangesAsync();

        var totals = await RoyaltyBalanceQuery.TotalsAsync(
            RoyaltyBalanceQuery.Apply(RoyaltyBalanceQuery.Build(_db.Context), new RoyaltyBalanceFilter()));

        var declared = totals.Single(t => t.Situation == RoyaltySituation.Declared);
        Assert.Equal(150m, declared.Balance);
        Assert.Equal(2, declared.Count);
        Assert.Equal(30m, totals.Single(t => t.Situation == RoyaltySituation.Conventional).Balance);
    }
}
