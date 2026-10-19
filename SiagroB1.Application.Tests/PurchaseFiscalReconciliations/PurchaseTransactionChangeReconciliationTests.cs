using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Queries;
using static SiagroB1.Application.Tests.PurchaseFiscalReconciliations.FiscalReconciliationSeed;

namespace SiagroB1.Application.Tests.PurchaseFiscalReconciliations;

/// <summary>Troca de fornecedor/armazém: o estorno gerado não é conciliável e o original perde a quantidade trocada.</summary>
public class PurchaseTransactionChangeReconciliationTests
{
    private static FiscalReconciliationFilter Filter() => new() { CardCode = Supplier, ItemCode = Product };

    /// <summary>Registra a troca de <paramref name="quantity"/> do original e gera o estorno (tipo 9) marcado.</summary>
    private static async Task<StorageTransaction> ChangeAsync(FiscalReconciliationSeed seed, StorageTransaction original, decimal quantity)
    {
        var change = new PurchaseTransactionChange
        {
            Key = Guid.NewGuid(), OriginalStorageTransactionKey = original.Key, ReturnStorageTransactionKey = Guid.NewGuid(),
            NewStorageTransactionKey = Guid.NewGuid(), PurchaseContractKey = Guid.NewGuid(), OriginalCardCode = Supplier,
            OriginalWarehouseCode = "PORTO", NewCardCode = "F2", NewWarehouseCode = "X", Quantity = quantity,
            AllocatedVolume = quantity, Reason = "teste",
        };
        var reversal = await seed.AddTransactionAsync(quantity, StorageTransactionType.PurchaseReturn);
        reversal.PurchaseTransactionChangeKey = change.Key;
        change.ReturnStorageTransactionKey = reversal.Key;
        seed.Db.Context.PurchaseTransactionChanges.Add(change);
        await seed.Db.SaveChangesAsync();
        return reversal;
    }

    [Fact]
    public async Task ChangeReturn_NotListed()
    {
        var seed = await FiscalReconciliationSeed.CreateAsync();
        var original = await seed.AddTransactionAsync(30_000m);
        await ChangeAsync(seed, original, 20_000m);

        var row = Assert.Single(await PurchaseFiscalReconciliationQuery.LoadTransactionsAsync(seed.Db.Context, Filter()));

        Assert.Equal(original.Key, row.Key);
    }

    [Fact]
    public async Task ChangeReturn_CannotBeLinked()
    {
        var seed = await FiscalReconciliationSeed.CreateAsync();
        var original = await seed.AddTransactionAsync(30_000m);
        var reversal = await ChangeAsync(seed, original, 20_000m);
        var (_, line) = await seed.AddInvoiceAsync(20_000m, type: PurchaseInvoiceType.Return);
        var service = FiscalReconciliationSeed.NewService(seed.Db);

        var ex = await Assert.ThrowsAsync<DefaultException>(() => service.SaveAsync(
            [new() { PurchaseInvoiceItemKey = line.Key!.Value, StorageTransactionKey = reversal.Key, Quantity = 20_000m }], "ana"));

        Assert.Equal("Estorno de troca de fornecedor/armazém não recebe nota fiscal.", ex.Message);
    }

    [Fact]
    public async Task FullyChangedOriginal_NotListed()
    {
        var seed = await FiscalReconciliationSeed.CreateAsync();
        var original = await seed.AddTransactionAsync(30_000m);
        await ChangeAsync(seed, original, 30_000m);

        Assert.Empty(await PurchaseFiscalReconciliationQuery.LoadTransactionsAsync(seed.Db.Context, Filter()));
    }

    [Fact]
    public async Task FullyChangedOriginal_WithLink_StaysListedWithZeroPhysical()
    {
        var seed = await FiscalReconciliationSeed.CreateAsync();
        var original = await seed.AddTransactionAsync(30_000m);
        var (_, line) = await seed.AddInvoiceAsync(30_000m);
        await seed.LinkAsync(line, original, 30_000m);
        await ChangeAsync(seed, original, 30_000m);

        var row = Assert.Single(await PurchaseFiscalReconciliationQuery.LoadTransactionsAsync(seed.Db.Context, Filter()));

        Assert.Equal(0m, row.PhysicalQuantity);
        Assert.Equal(30_000m, row.FiscalQuantity);
    }

    [Fact]
    public async Task PartiallyChangedOriginal_PhysicalIsRemaining()
    {
        var seed = await FiscalReconciliationSeed.CreateAsync();
        var original = await seed.AddTransactionAsync(30_000m);
        await ChangeAsync(seed, original, 20_000m);

        var row = Assert.Single(await PurchaseFiscalReconciliationQuery.LoadTransactionsAsync(seed.Db.Context, Filter()));

        Assert.Equal(10_000m, row.PhysicalQuantity);
    }

    [Fact]
    public async Task Planner_DoesNotSuggestChangeReturn()
    {
        var seed = await FiscalReconciliationSeed.CreateAsync();
        var original = await seed.AddTransactionAsync(30_000m);
        var reversal = await ChangeAsync(seed, original, 20_000m);
        var (_, line) = await seed.AddInvoiceAsync(20_000m, type: PurchaseInvoiceType.Return);
        var service = FiscalReconciliationSeed.NewService(seed.Db);

        var pairs = await service.SuggestAsync([line.Key!.Value], [reversal.Key]);

        Assert.Empty(pairs);
    }

    [Fact]
    public async Task Planner_UsesRemainingBalanceOfPartiallyChangedOriginal()
    {
        var seed = await FiscalReconciliationSeed.CreateAsync();
        var first = await seed.AddTransactionAsync(30_000m, date: new DateTime(2026, 10, 1));
        var second = await seed.AddTransactionAsync(30_000m, date: new DateTime(2026, 10, 2));
        await ChangeAsync(seed, first, 20_000m);
        var (_, line) = await seed.AddInvoiceAsync(30_000m);
        var service = FiscalReconciliationSeed.NewService(seed.Db);

        var pairs = await service.SuggestAsync([line.Key!.Value], [first.Key, second.Key]);

        // O primeiro só tem 10.000 em aberto (30.000 - 20.000 trocados); o resto da NF vai para o segundo.
        Assert.Equal(10_000m, pairs.Single(p => p.StorageTransactionKey == first.Key).Quantity);
        Assert.Equal(20_000m, pairs.Single(p => p.StorageTransactionKey == second.Key).Quantity);
    }
}
