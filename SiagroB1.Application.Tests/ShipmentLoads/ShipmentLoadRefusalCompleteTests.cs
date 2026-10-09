using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Application.Tests.ShipmentLoads;

/// <summary>Segundo tempo da recusa (spec 2026-10-09 §5.3): a confirmação da última devolução conclui.</summary>
public class ShipmentLoadRefusalCompleteTests
{
    private static async Task<(NfeLoadRefusalScenario S, List<Guid> Returns)> RefuseAsync(
        int documents, decimal quantity, RefusalDestination destination, string? warehouse = null)
    {
        var s = await NfeLoadRefusalTestSeed.SeedAsync(documents);
        await NfeLoadRefusalTestSeed.RefuseService(s.Db).ExecuteAsync(
            new RefusalRequest(s.Load.Key, s.SaleKeys.Select(k => new RefusalLine(k, quantity)).ToList(),
                destination, warehouse, "Recusado por umidade"),
            "tester");

        var returns = await s.Db.Context.SalesInvoices.AsNoTracking()
            .Where(i => i.InvoiceType == SalesInvoiceType.Return)
            .OrderBy(i => i.InvoiceNumber)
            .Select(i => i.Key)
            .ToListAsync();
        s.Db.Context.ChangeTracker.Clear();
        return (s, returns);
    }

    private static Task ConfirmAsync(NfeLoadRefusalScenario s, Guid returnKey) =>
        NfeLoadRefusalTestSeed.AuthorizeAndConfirmAsync(
            s.Db, returnKey, NfeLoadRefusalTestSeed.ConfirmService(s.Db, NfeLoadRefusalTestSeed.Complete(s.Db)));

    private static Task<ShipmentLoad> LoadAsync(NfeLoadRefusalScenario s) =>
        s.Db.Context.ShipmentLoads.AsNoTracking().SingleAsync();

    private static Task<ShipmentLoadRefusal> RefusalAsync(NfeLoadRefusalScenario s) =>
        s.Db.Context.ShipmentLoadRefusals.AsNoTracking().SingleAsync();

    [Fact]
    public async Task First_of_two_returns_does_not_complete()
    {
        var (s, returns) = await RefuseAsync(2, 10_000m, RefusalDestination.Warehouse, NfeLoadRefusalTestSeed.DestinationWarehouse);

        await ConfirmAsync(s, returns[0]);

        Assert.Equal(ShipmentLoadRefusalStatus.Pending, (await RefusalAsync(s)).Status);
        var load = await LoadAsync(s);
        Assert.Equal(ShipmentLoadStatus.RefusalPending, load.Status);
        Assert.Equal(50_000m, load.InvoicedQuantity); // o saldo daquele documento já voltou
        Assert.False(await s.Db.Context.StorageTransactions.AnyAsync(t => t.TransactionType == StorageTransactionType.SalesShipmentReturn));
    }

    [Fact]
    public async Task Last_return_to_warehouse_creates_the_entry_and_unlocks()
    {
        var (s, returns) = await RefuseAsync(2, 10_000m, RefusalDestination.Warehouse, NfeLoadRefusalTestSeed.DestinationWarehouse);

        await ConfirmAsync(s, returns[0]);
        s.Db.Context.ChangeTracker.Clear();
        await ConfirmAsync(s, returns[1]);

        var refusal = await RefusalAsync(s);
        Assert.Equal(ShipmentLoadRefusalStatus.Completed, refusal.Status);
        Assert.Equal("tester", refusal.CompletedBy);
        Assert.NotNull(refusal.CompletedAt);

        var entry = await s.Db.Context.StorageTransactions.AsNoTracking()
            .SingleAsync(t => t.TransactionType == StorageTransactionType.SalesShipmentReturn);
        Assert.Equal(StorageTransactionsStatus.Confirmed, entry.TransactionStatus);
        Assert.Equal(NfeLoadRefusalTestSeed.DestinationWarehouse, entry.WarehouseCode);
        Assert.Equal(20_000m, entry.NetWeight);
        Assert.Equal(s.Load.Key, entry.RefusedFromShipmentLoadKey);
        // As três chaves proibidas (ver ShipmentLoadRefusalEffectsService).
        Assert.Null(entry.ShipmentLoadKey);
        Assert.Null(entry.ShipmentReleaseKey);
        Assert.Null(entry.ReturnInvoiceKey);

        var load = await LoadAsync(s);
        Assert.Equal(40_000m, load.InvoicedQuantity);
        Assert.Equal(20_000m, load.ReturnedToWarehouseQuantity);
        Assert.Equal(ShipmentLoadStatus.Returned, load.Status);
    }

    [Fact]
    public async Task Rebilling_puts_the_load_back_on_billing()
    {
        var (s, returns) = await RefuseAsync(1, 10_000m, RefusalDestination.Rebilling);

        await ConfirmAsync(s, returns[0]);

        var load = await LoadAsync(s);
        Assert.Equal(ShipmentLoadStatus.PartiallyInvoiced, load.Status);
        Assert.Equal(10_000m, load.AvailableQuantity);
        Assert.Equal(ShipmentLoadRefusalStatus.Completed, (await RefusalAsync(s)).Status);
    }

    [Fact]
    public async Task Transshipment_opens_with_the_refused_quantity_even_though_the_load_was_locked()
    {
        // Review Focus 2: a carga está em RefusalPending; a conclusão não pode passar por EnsureLoadAcceptsTransshipment.
        var (s, returns) = await RefuseAsync(1, 10_000m, RefusalDestination.Transshipment, NfeLoadRefusalTestSeed.DestinationWarehouse);
        Assert.Equal(ShipmentLoadStatus.RefusalPending, (await LoadAsync(s)).Status);

        await ConfirmAsync(s, returns[0]);

        var transshipment = await s.Db.Context.ShipmentLoadsTransshipments.AsNoTracking().SingleAsync();
        Assert.Equal(10_000m, transshipment.OutgoingQuantity);
        Assert.Equal(TransshipmentOrigin.Refusal, transshipment.Origin);
        Assert.Equal(NfeLoadRefusalTestSeed.DestinationWarehouse, transshipment.WarehouseCode);
        Assert.Equal(ShipmentLoadStatus.InTransshipment, (await LoadAsync(s)).Status);
        Assert.Equal(ShipmentLoadRefusalStatus.Completed, (await RefusalAsync(s)).Status);
    }

    [Fact]
    public async Task Failure_in_the_effects_fails_the_confirmation_and_rolls_its_transaction_back()
    {
        // O armazém do registro é válido (o registro o resolve); a falha é provocada só no SaveChanges do romaneio
        // de devolução, dentro dos efeitos da conclusão — StorageTransactionsCreateService não valida o armazém.
        var (s, returns) = await RefuseAsync(1, 10_000m, RefusalDestination.Warehouse, NfeLoadRefusalTestSeed.DestinationWarehouse);
        var invoice = await s.Db.Context.SalesInvoices.SingleAsync(i => i.Key == returns[0]);
        invoice.NfeStatus = NfeStatus.Authorized;
        await s.Db.SaveChangesAsync();

        var failing = new CountingUnitOfWork(TestDb.CreateUnitOfWork(s.DatabaseName, new ThrowOnSaveInterceptor(
            e => e.Entity is StorageTransaction { TransactionType: StorageTransactionType.SalesShipmentReturn } &&
                 e.State == EntityState.Added)));
        var confirm = NfeLoadRefusalTestSeed.ConfirmService(failing, NfeLoadRefusalTestSeed.Complete(failing));

        await Assert.ThrowsAsync<DbUpdateException>(() => confirm.ExecuteAsync(returns[0], "tester"));

        // ⚠️ O InMemory não tem transação: o que já foi salvo antes da falha (devolução Confirmada, recusa Concluída)
        // fica no banco de teste. A atomicidade é provada pelo protocolo — a confirmação é a dona da transação e a
        // desfaz, sem commit; o rollback real no SQL Server é provado no E2E (Task 11).
        Assert.Equal(1, failing.Begins);
        Assert.Equal(0, failing.Commits);
        Assert.Equal(1, failing.Rollbacks);
        Assert.False(await s.Db.Context.StorageTransactions.AsNoTracking()
            .AnyAsync(t => t.TransactionType == StorageTransactionType.SalesShipmentReturn));
    }

    [Fact]
    public async Task Confirmation_without_the_complete_service_keeps_the_refusal_pending()
    {
        // Os testes antigos e o fluxo síncrono constroem a confirmação sem a conclusão: nada muda para eles.
        var (s, returns) = await RefuseAsync(1, 10_000m, RefusalDestination.Rebilling);

        await NfeLoadRefusalTestSeed.AuthorizeAndConfirmAsync(s.Db, returns[0]);

        Assert.Equal(ShipmentLoadRefusalStatus.Pending, (await RefusalAsync(s)).Status);
        Assert.Equal(ShipmentLoadStatus.RefusalPending, (await LoadAsync(s)).Status);
    }

    private static async Task CancelNfeAsync(NfeLoadRefusalScenario s, Guid returnKey)
    {
        // O que o 2b faz: a SEFAZ cancelou a NF-e, e o documento acompanha (CancelAfterNfeAsync, transação própria).
        var invoice = await s.Db.Context.SalesInvoices.SingleAsync(i => i.Key == returnKey);
        invoice.NfeStatus = NfeStatus.Cancelled;
        await s.Db.SaveChangesAsync();
        s.Db.Context.ChangeTracker.Clear();

        await NfeLoadRefusalTestSeed.CancelService(s.Db, NfeLoadRefusalTestSeed.Complete(s.Db))
            .CancelAfterNfeAsync(returnKey, "tester");
    }

    [Fact]
    public async Task Nfe_cancellation_of_the_open_return_completes_with_the_confirmed_ones()
    {
        // A confirmada; B autorizada mas sem confirmação (falhou ou ainda não veio) e cancelada pelo 2b.
        var (s, returns) = await RefuseAsync(2, 10_000m, RefusalDestination.Warehouse, NfeLoadRefusalTestSeed.DestinationWarehouse);
        await ConfirmAsync(s, returns[0]);
        s.Db.Context.ChangeTracker.Clear();
        (await s.Db.Context.SalesInvoices.SingleAsync(i => i.Key == returns[1])).NfeStatus = NfeStatus.Authorized;
        await s.Db.SaveChangesAsync();
        s.Db.Context.ChangeTracker.Clear();

        await CancelNfeAsync(s, returns[1]);

        var refusal = await RefusalAsync(s);
        Assert.Equal(ShipmentLoadRefusalStatus.Completed, refusal.Status);
        Assert.Equal("tester", refusal.CompletedBy);

        var entry = await s.Db.Context.StorageTransactions.AsNoTracking()
            .SingleAsync(t => t.TransactionType == StorageTransactionType.SalesShipmentReturn);
        Assert.Equal(10_000m, entry.NetWeight); // só a devolução confirmada (A)

        var load = await LoadAsync(s);
        Assert.NotEqual(ShipmentLoadStatus.RefusalPending, load.Status);
        Assert.Equal(10_000m, load.ReturnedToWarehouseQuantity);
    }

    [Fact]
    public async Task Nfe_cancellation_of_the_only_return_cancels_the_refusal()
    {
        var (s, returns) = await RefuseAsync(1, 10_000m, RefusalDestination.Rebilling);
        (await s.Db.Context.SalesInvoices.SingleAsync(i => i.Key == returns[0])).NfeStatus = NfeStatus.Authorized;
        await s.Db.SaveChangesAsync();
        s.Db.Context.ChangeTracker.Clear();

        await CancelNfeAsync(s, returns[0]);

        var refusal = await RefusalAsync(s);
        Assert.Equal(ShipmentLoadRefusalStatus.Cancelled, refusal.Status);
        Assert.Equal("tester", refusal.CancelledBy);
        Assert.NotNull(refusal.CancelledAt);
        Assert.Equal(ShipmentLoadStatus.Invoiced, (await LoadAsync(s)).Status);
        Assert.Contains(await s.Db.Context.ShipmentLoadMovements.AsNoTracking().ToListAsync(),
            m => m.MovementType == ShipmentLoadMovementType.RefusalCancelled);
        Assert.False(await s.Db.Context.StorageTransactions.AnyAsync(t => t.TransactionType == StorageTransactionType.SalesShipmentReturn));
    }

    [Fact]
    public async Task Nfe_cancellation_of_a_return_that_completed_the_refusal_is_refused_before_sefaz()
    {
        // Concluída, a recusa já gerou entrada no armazém/liberações/transbordo: cancelar a NF-e deixaria tudo para trás.
        var (s, returns) = await RefuseAsync(1, 10_000m, RefusalDestination.Rebilling);
        await ConfirmAsync(s, returns[0]);
        s.Db.Context.ChangeTracker.Clear();
        Assert.Equal(ShipmentLoadRefusalStatus.Completed, (await RefusalAsync(s)).Status);

        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            NfeLoadRefusalTestSeed.CancelService(s.Db).EnsureCanCancelAsync(returns[0]));

        Assert.Equal("Esta devolução concluiu a recusa da carga CG000001: a NF-e não pode ser cancelada pelo Siagro.", ex.Message);
    }
}
