using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.ShipmentReleases;
using SiagroB1.Application.Services.StorageTransactions;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Interceptors;

namespace SiagroB1.Application.Tests.PurchaseFiscalReconciliations;

/// <summary>
/// A conciliação nunca trava o fluxo (D1): cancelar/excluir a NF, remover a linha ou cancelar o romaneio solta os
/// vínculos sozinho. As duas únicas recusas fora da tela: reduzir a quantidade abaixo do vinculado e trocar o produto.
/// </summary>
public class PurchaseFiscalReconciliationCleanupInterceptorTests
{
    private static async Task<(FiscalReconciliationSeed Seed, StorageTransaction Tx, StorageTransaction Other, PurchaseInvoice Invoice, PurchaseInvoiceItem Line)> LinkedAsync()
    {
        var seed = await FiscalReconciliationSeed.CreateAsync(TestDb.CreateUnitOfWork(new PurchaseFiscalReconciliationCleanupInterceptor()));
        var tx = await seed.AddTransactionAsync(30_000m);
        var other = await seed.AddTransactionAsync(30_000m);
        var (invoice, line) = await seed.AddInvoiceAsync(60_000m);
        await seed.LinkAsync(line, tx, 30_000m);
        await seed.LinkAsync(line, other, 30_000m);
        other.FiscalReconciledAt = DateTime.Now;
        await seed.Db.SaveChangesAsync();
        return (seed, tx, other, invoice, line);
    }

    [Fact]
    public async Task Cancelling_a_transaction_removes_its_links_only()
    {
        var (seed, tx, other, _, _) = await LinkedAsync();

        tx.TransactionStatus = StorageTransactionsStatus.Cancelled;
        await seed.Db.SaveChangesAsync();

        var link = Assert.Single(seed.Db.Context.PurchaseFiscalReconciliations.AsNoTracking());
        Assert.Equal(other.Key, link.StorageTransactionKey);
        Assert.NotNull((await seed.Db.Context.StorageTransactions.AsNoTracking().SingleAsync(t => t.Key == other.Key)).FiscalReconciledAt);
    }

    [Fact]
    public async Task Cancelling_the_invoice_removes_all_its_links_and_confirmations()
    {
        var (seed, _, other, invoice, _) = await LinkedAsync();

        invoice.InvoiceStatus = InvoiceStatus.Cancelled;
        await seed.Db.SaveChangesAsync();

        Assert.Empty(seed.Db.Context.PurchaseFiscalReconciliations.AsNoTracking());
        Assert.Null((await seed.Db.Context.StorageTransactions.AsNoTracking().SingleAsync(t => t.Key == other.Key)).FiscalReconciledAt);
    }

    [Fact]
    public async Task Deleting_the_invoice_and_its_line_removes_the_links()
    {
        var (seed, _, _, invoice, line) = await LinkedAsync();
        // Como no serviço de exclusão: nota e linhas carregadas, vínculos NÃO rastreados.
        seed.Db.Context.ChangeTracker.Clear();

        seed.Db.Context.PurchaseInvoicesItems.Remove(await seed.Db.Context.PurchaseInvoicesItems.SingleAsync(l => l.Key == line.Key));
        seed.Db.Context.PurchaseInvoices.Remove(await seed.Db.Context.PurchaseInvoices.SingleAsync(i => i.Key == invoice.Key));
        await seed.Db.SaveChangesAsync();

        Assert.Empty(seed.Db.Context.PurchaseFiscalReconciliations.AsNoTracking());
    }

    [Fact]
    public async Task Deleting_only_the_line_removes_its_links()
    {
        var (seed, _, _, _, line) = await LinkedAsync();
        seed.Db.Context.ChangeTracker.Clear();

        seed.Db.Context.PurchaseInvoicesItems.Remove(await seed.Db.Context.PurchaseInvoicesItems.SingleAsync(l => l.Key == line.Key));
        await seed.Db.SaveChangesAsync();

        Assert.Empty(seed.Db.Context.PurchaseFiscalReconciliations.AsNoTracking());
    }

    [Fact]
    public async Task Reducing_line_quantity_below_linked_is_refused_and_above_is_allowed()
    {
        var (seed, _, _, _, line) = await LinkedAsync();

        line.Quantity = 59_000m;
        var error = await Assert.ThrowsAsync<DefaultException>(() => seed.Db.SaveChangesAsync());
        Assert.Contains("desvincule", error.Message);

        line.Quantity = 61_000m;
        await seed.Db.SaveChangesAsync();
    }

    [Fact]
    public async Task Changing_product_or_unit_of_a_linked_line_is_refused()
    {
        var (seed, _, _, _, line) = await LinkedAsync();

        line.ItemCode = "MILHO";
        Assert.Contains("produto", (await Assert.ThrowsAsync<DefaultException>(() => seed.Db.SaveChangesAsync())).Message);

        line.ItemCode = FiscalReconciliationSeed.Product;
        line.UnitOfMeasureCode = "TON";
        Assert.Contains("desvincule", (await Assert.ThrowsAsync<DefaultException>(() => seed.Db.SaveChangesAsync())).Message);
    }

    [Fact]
    public async Task Unlinked_line_edits_are_not_touched()
    {
        var seed = await FiscalReconciliationSeed.CreateAsync(TestDb.CreateUnitOfWork(new PurchaseFiscalReconciliationCleanupInterceptor()));
        var (_, line) = await seed.AddInvoiceAsync(100m);

        line.Quantity = 1m;
        line.ItemCode = "MILHO";
        await seed.Db.SaveChangesAsync();
    }

    /// <summary>Ponta a ponta: o cancelamento real de romaneio passa pelo interceptor sem saber dele.</summary>
    [Fact]
    public async Task Real_cancel_service_releases_the_links()
    {
        var (seed, tx, _, _, _) = await LinkedAsync();
        tx.TransactionOrigin = TransactionCode.StorageTransaction;
        await seed.Db.SaveChangesAsync();
        var cancel = new StorageTransactionsCancelService(seed.Db, new ShipmentReleasesRecalculateShippedService(seed.Db.Context));

        await cancel.ExecuteAsync(tx.Key, "ana");

        Assert.DoesNotContain(seed.Db.Context.PurchaseFiscalReconciliations.AsNoTracking(), l => l.StorageTransactionKey == tx.Key);
    }
}
