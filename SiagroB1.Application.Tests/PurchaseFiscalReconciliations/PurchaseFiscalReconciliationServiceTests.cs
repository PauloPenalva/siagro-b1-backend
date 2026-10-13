using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.PurchaseFiscalReconciliations;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using static SiagroB1.Application.Tests.PurchaseFiscalReconciliations.FiscalReconciliationSeed;

namespace SiagroB1.Application.Tests.PurchaseFiscalReconciliations;

public class PurchaseFiscalReconciliationServiceTests
{
    private static FiscalReconciliationPairDto Pair(PurchaseInvoiceItem line, StorageTransaction tx, decimal qty) =>
        new() { PurchaseInvoiceItemKey = line.Key!.Value, StorageTransactionKey = tx.Key, Quantity = qty };

    private static FiscalReconciliationFilter Filter() => new() { CardCode = Supplier, ItemCode = Product };

    private static async Task<string> RefusalAsync(Func<Task> act) =>
        (await Assert.ThrowsAsync<DefaultException>(act)).Message;

    [Fact]
    public async Task Save_allows_fiscal_above_physical_and_status_is_partial()
    {
        var seed = await FiscalReconciliationSeed.CreateAsync();
        var tx = await seed.AddTransactionAsync(29_820m);
        var (_, line) = await seed.AddInvoiceAsync(30_000m);
        var service = new PurchaseFiscalReconciliationService(seed.Db);

        await service.SaveAsync([Pair(line, tx, 30_000m)], "ana");

        var row = Assert.Single(await service.ListTransactionsAsync(Filter()));
        Assert.Equal(180m, row.Difference);
        Assert.Equal(FiscalReconciliationStatus.Partial, row.Status);
        Assert.Equal("ana", (await seed.Db.Context.PurchaseFiscalReconciliations.SingleAsync()).CreatedBy);
    }

    [Fact]
    public async Task Save_upserts_and_zero_removes_and_clears_confirmation()
    {
        var seed = await FiscalReconciliationSeed.CreateAsync();
        var tx = await seed.AddTransactionAsync(100m);
        var (_, line) = await seed.AddInvoiceAsync(100m);
        await seed.LinkAsync(line, tx, 40m);
        tx.FiscalReconciledAt = DateTime.Now;
        tx.FiscalReconciledBy = "ana";
        await seed.Db.SaveChangesAsync();
        var service = new PurchaseFiscalReconciliationService(seed.Db);

        await service.SaveAsync([Pair(line, tx, 70m)], "bia");
        Assert.Equal(70m, (await seed.Db.Context.PurchaseFiscalReconciliations.AsNoTracking().SingleAsync()).Quantity);
        Assert.Null((await seed.Db.Context.StorageTransactions.AsNoTracking().SingleAsync(t => t.Key == tx.Key)).FiscalReconciledAt);

        await service.SaveAsync([Pair(line, tx, 0m)], "bia");
        Assert.Empty(seed.Db.Context.PurchaseFiscalReconciliations.AsNoTracking());
    }

    [Fact]
    public async Task Save_refuses_when_lines_total_exceeds_invoice_quantity_and_saves_nothing()
    {
        var seed = await FiscalReconciliationSeed.CreateAsync();
        var t1 = await seed.AddTransactionAsync(30_000m);
        var t2 = await seed.AddTransactionAsync(30_000m);
        var (_, line) = await seed.AddInvoiceAsync(50_000m, taxNumber: "777");
        var service = new PurchaseFiscalReconciliationService(seed.Db);

        var message = await RefusalAsync(() => service.SaveAsync([Pair(line, t1, 30_000m), Pair(line, t2, 30_000m)], "ana"));

        Assert.Contains("777-1", message);
        Assert.Contains("passa da quantidade da linha", message);
        Assert.Empty(seed.Db.Context.PurchaseFiscalReconciliations.AsNoTracking());
    }

    [Fact]
    public async Task Save_converts_commercial_unit_and_refuses_without_factor()
    {
        var seed = await FiscalReconciliationSeed.CreateAsync();
        var tx = await seed.AddTransactionAsync(6_000m);
        var (_, sc) = await seed.AddInvoiceAsync(100m, unit: "SC");
        var service = new PurchaseFiscalReconciliationService(seed.Db);

        Assert.Contains("Cadastre o fator da unidade SC", await RefusalAsync(() => service.SaveAsync([Pair(sc, tx, 6_000m)], "ana")));

        seed.Db.Context.ItemComplements.Add(new ItemComplement { ItemCode = Product, CommercialUnitOfMeasureCode = "SC", CommercialFactor = 60m });
        await seed.Db.SaveChangesAsync();
        await service.SaveAsync([Pair(sc, tx, 6_000m)], "ana");
        Assert.Single(seed.Db.Context.PurchaseFiscalReconciliations.AsNoTracking());
    }

    [Fact]
    public async Task Save_refuses_cancelled_invoice_counter_note_wrong_sense_product_and_contract()
    {
        var seed = await FiscalReconciliationSeed.CreateAsync();
        var c1 = await seed.AddContractAsync("PC-1");
        var c2 = await seed.AddContractAsync("PC-2");
        var tx = await seed.AddTransactionAsync(100m, contract: c1, code: "RM-1");
        var ret = await seed.AddTransactionAsync(100m, StorageTransactionType.PurchaseReturn, code: "RM-2");
        var cancelledTx = await seed.AddTransactionAsync(100m, status: StorageTransactionsStatus.Cancelled, code: "RM-3");
        var receipt = await seed.AddTransactionAsync(100m, StorageTransactionType.Receipt, code: "RM-4");
        var service = new PurchaseFiscalReconciliationService(seed.Db);

        var (_, cancelled) = await seed.AddInvoiceAsync(100m, status: InvoiceStatus.Cancelled);
        Assert.Contains("cancelad", await RefusalAsync(() => service.SaveAsync([Pair(cancelled, tx, 10m)], "a")));

        var (_, counter) = await seed.AddInvoiceAsync(100m, producerInvoiceKey: Guid.NewGuid());
        Assert.Contains("contra-nota", await RefusalAsync(() => service.SaveAsync([Pair(counter, tx, 10m)], "a")));

        var (_, normal) = await seed.AddInvoiceAsync(100m);
        Assert.Contains("NF de devolução", await RefusalAsync(() => service.SaveAsync([Pair(normal, ret, 10m)], "a")));
        Assert.Contains("RM-3", await RefusalAsync(() => service.SaveAsync([Pair(normal, cancelledTx, 10m)], "a")));
        Assert.Contains("não é romaneio de compra", await RefusalAsync(() => service.SaveAsync([Pair(normal, receipt, 10m)], "a")));

        var (_, milho) = await seed.AddInvoiceAsync(100m, itemCode: "MILHO");
        Assert.Contains("produto", await RefusalAsync(() => service.SaveAsync([Pair(milho, tx, 10m)], "a")));

        var (_, otherContract) = await seed.AddInvoiceAsync(100m, contract: c2);
        Assert.Contains("PC-2", await RefusalAsync(() => service.SaveAsync([Pair(otherContract, tx, 10m)], "a")));

        Assert.Contains("negativa", await RefusalAsync(() => service.SaveAsync([Pair(normal, tx, -1m)], "a")));
        Assert.Empty(seed.Db.Context.PurchaseFiscalReconciliations.AsNoTracking());
    }

    [Fact]
    public async Task Save_refuses_custody_note()
    {
        var seed = await FiscalReconciliationSeed.CreateAsync();
        var tx = await seed.AddTransactionAsync(100m);
        var (_, custody) = await seed.AddInvoiceAsync(100m, custodyTransactionKey: Guid.NewGuid());

        Assert.Contains("custódia", await RefusalAsync(() =>
            new PurchaseFiscalReconciliationService(seed.Db).SaveAsync([Pair(custody, tx, 10m)], "a")));
    }

    [Fact]
    public async Task Save_accepts_other_issuer_without_refusing()
    {
        var seed = await FiscalReconciliationSeed.CreateAsync();
        await seed.AddPartnerAsync("F0003", "Outro produtor", "98765432100");
        var tx = await seed.AddTransactionAsync(100m);
        var (_, line) = await seed.AddInvoiceAsync(100m, cardCode: "F0003");

        await new PurchaseFiscalReconciliationService(seed.Db).SaveAsync([Pair(line, tx, 100m)], "a");

        Assert.Single(seed.Db.Context.PurchaseFiscalReconciliations.AsNoTracking());
    }

    [Fact]
    public async Task Suggest_adds_current_quantity_and_uses_open_balance()
    {
        var seed = await FiscalReconciliationSeed.CreateAsync();
        var t1 = await seed.AddTransactionAsync(30_000m, date: new DateTime(2026, 10, 1));
        var t2 = await seed.AddTransactionAsync(30_000m, date: new DateTime(2026, 10, 2));
        var (_, l1) = await seed.AddInvoiceAsync(60_100m, taxNumber: "1");
        await seed.LinkAsync(l1, t1, 10_000m);
        var service = new PurchaseFiscalReconciliationService(seed.Db);

        var pairs = await service.SuggestAsync([l1.Key!.Value], [t1.Key, t2.Key]);

        // disponível 50.100: 20.000 completa o t1 (atual 10.000 → 30.000), 30.100 vai para o t2 (último, leva a sobra).
        Assert.Equal(2, pairs.Count);
        var p1 = pairs.Single(p => p.StorageTransactionKey == t1.Key);
        Assert.Equal((10_000m, 30_000m), (p1.CurrentQuantity, p1.Quantity));
        Assert.Equal(30_100m, pairs.Single(p => p.StorageTransactionKey == t2.Key).Quantity);
    }

    [Fact]
    public async Task Confirm_needs_a_link_and_unconfirm_clears()
    {
        var seed = await FiscalReconciliationSeed.CreateAsync();
        var tx = await seed.AddTransactionAsync(100m);
        var service = new PurchaseFiscalReconciliationService(seed.Db);

        Assert.Contains("Vincule ao menos uma NF", await RefusalAsync(() => service.ConfirmAsync(tx.Key, "ana")));

        var (_, line) = await seed.AddInvoiceAsync(90m);
        await seed.LinkAsync(line, tx, 90m);
        await service.ConfirmAsync(tx.Key, "ana");
        var row = Assert.Single(await service.ListTransactionsAsync(Filter()));
        Assert.Equal(FiscalReconciliationStatus.Reconciled, row.Status);
        Assert.Equal("ana", row.FiscalReconciledBy);

        await service.UnconfirmAsync(tx.Key);
        Assert.Equal(FiscalReconciliationStatus.Partial, Assert.Single(await service.ListTransactionsAsync(Filter())).Status);
    }

    [Fact]
    public async Task Remove_deletes_link_and_clears_confirmation()
    {
        var seed = await FiscalReconciliationSeed.CreateAsync();
        var tx = await seed.AddTransactionAsync(100m);
        var (_, line) = await seed.AddInvoiceAsync(100m);
        var link = await seed.LinkAsync(line, tx, 100m);
        var service = new PurchaseFiscalReconciliationService(seed.Db);
        await service.ConfirmAsync(tx.Key, "ana");

        await service.RemoveAsync(link.Key);

        Assert.Empty(seed.Db.Context.PurchaseFiscalReconciliations.AsNoTracking());
        Assert.Null((await seed.Db.Context.StorageTransactions.AsNoTracking().SingleAsync(t => t.Key == tx.Key)).FiscalReconciledAt);
        await Assert.ThrowsAsync<NotFoundException>(() => service.RemoveAsync(link.Key));
    }

    [Fact]
    public async Task List_requires_supplier_and_item()
    {
        var service = new PurchaseFiscalReconciliationService((await FiscalReconciliationSeed.CreateAsync()).Db);

        Assert.Contains("fornecedor e o produto", await RefusalAsync(() => service.ListTransactionsAsync(new() { CardCode = Supplier })));
    }
}
