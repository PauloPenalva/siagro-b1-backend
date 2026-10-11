using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.PurchaseInvoices;
using SiagroB1.Application.Services.StorageDeposits;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.StorageDeposits;

public class StorageReturnReconciliationsServiceTests
{
    private static StorageReturnReconciliationsService Service(UnitOfWork db, string erp = "STANDALONE") =>
        new(db, TestConfiguration.Erp(erp));

    [Fact]
    public async Task Lists_unreconciled_shipments_of_the_remittance_release()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageDepositSeed.CreateAsync(db);
        var remittance = await seed.AddRemittanceAsync(100_000m);
        var ret = await seed.AddReturnAsync(remittance, 30_000m);
        var s1 = await seed.AddShipmentAsync(15_000m);
        var s2 = await seed.AddShipmentAsync(15_000m);

        var rows = await Service(db).ListAsync(ret.Key);

        Assert.Equal(new[] { s1.Key, s2.Key }.OrderBy(x => x), rows.Select(r => r.Key).OrderBy(x => x));
        Assert.All(rows, r => Assert.False(r.IsReconciled));
    }

    [Fact]
    public async Task Reconcile_marks_rows_and_hides_them_from_other_returns()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageDepositSeed.CreateAsync(db);
        var remittance = await seed.AddRemittanceAsync(100_000m);
        var ret1 = await seed.AddReturnAsync(remittance, 30_000m);
        var ret2 = await seed.AddReturnAsync(remittance, 30_000m);
        var s1 = await seed.AddShipmentAsync(30_000m);

        await Service(db).ReconcileAsync(ret1.Key, [s1.Key], "tester");

        Assert.True((await Service(db).ListAsync(ret1.Key)).Single().IsReconciled);
        Assert.Empty(await Service(db).ListAsync(ret2.Key));
    }

    [Fact]
    public async Task Refuses_shipment_of_another_release()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageDepositSeed.CreateAsync(db);
        var remittance = await seed.AddRemittanceAsync(100_000m);
        var ret = await seed.AddReturnAsync(remittance, 30_000m);
        var foreign = await seed.AddShipmentAsync(30_000m);
        foreign.ShipmentReleaseKey = Guid.NewGuid();
        await db.SaveChangesAsync();

        var e = await Assert.ThrowsAsync<DefaultException>(() => Service(db).ReconcileAsync(ret.Key, [foreign.Key], "tester"));

        Assert.Equal($"O romaneio {foreign.Code} não é carregamento da liberação da remessa ou já está reconciliado.", e.Message);
    }

    [Fact]
    public async Task Refuses_already_reconciled_shipment()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageDepositSeed.CreateAsync(db);
        var remittance = await seed.AddRemittanceAsync(100_000m);
        var ret1 = await seed.AddReturnAsync(remittance, 30_000m);
        var ret2 = await seed.AddReturnAsync(remittance, 30_000m);
        var s1 = await seed.AddShipmentAsync(30_000m);
        await Service(db).ReconcileAsync(ret1.Key, [s1.Key], "tester");

        await Assert.ThrowsAsync<DefaultException>(() => Service(db).ReconcileAsync(ret2.Key, [s1.Key], "tester"));
    }

    [Fact]
    public async Task Refuses_cancelled_return()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageDepositSeed.CreateAsync(db);
        var remittance = await seed.AddRemittanceAsync(100_000m);
        var cancelled = await seed.AddReturnAsync(remittance, 30_000m, InvoiceStatus.Cancelled);
        var s1 = await seed.AddShipmentAsync(30_000m);

        var e = await Assert.ThrowsAsync<DefaultException>(() => Service(db).ReconcileAsync(cancelled.Key, [s1.Key], "tester"));
        Assert.Equal("Documento de entrada cancelado não pode ser reconciliado.", e.Message);
    }

    [Fact]
    public async Task Refuses_empty_selection()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageDepositSeed.CreateAsync(db);
        var ret = await seed.AddReturnAsync(await seed.AddRemittanceAsync(100_000m), 1m);

        var e = await Assert.ThrowsAsync<DefaultException>(() => Service(db).ReconcileAsync(ret.Key, [], "tester"));
        Assert.Equal("Selecione ao menos um romaneio.", e.Message);
    }

    [Fact]
    public async Task Undo_removes_the_link()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageDepositSeed.CreateAsync(db);
        var ret = await seed.AddReturnAsync(await seed.AddRemittanceAsync(100_000m), 30_000m);
        var s1 = await seed.AddShipmentAsync(30_000m);
        await Service(db).ReconcileAsync(ret.Key, [s1.Key], "tester");

        await Service(db).UndoAsync(ret.Key, s1.Key);

        Assert.False(await db.Context.StorageReturnReconciliations.AnyAsync());
    }

    [Fact]
    public async Task Refuses_in_sap_mode()
    {
        var db = TestDb.CreateUnitOfWork();
        var e = await Assert.ThrowsAsync<DefaultException>(() => Service(db, "SAPB1").ListAsync(Guid.NewGuid()));
        Assert.Equal("Disponível somente no modo STANDALONE.", e.Message);
    }

    [Fact]
    public async Task Cancelling_the_return_removes_its_reconciliations()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageDepositSeed.CreateAsync(db);
        var ret = await seed.AddReturnAsync(await seed.AddRemittanceAsync(100_000m), 30_000m);
        var s1 = await seed.AddShipmentAsync(30_000m);
        await Service(db).ReconcileAsync(ret.Key, [s1.Key], "tester");

        await new PurchaseInvoicesCancelService(db).ExecuteAsync(ret.Key, "tester");

        Assert.False(await db.Context.StorageReturnReconciliations.AnyAsync());
    }

    [Fact]
    public async Task Deleting_the_return_removes_its_reconciliations()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageDepositSeed.CreateAsync(db);
        var ret = await seed.AddReturnAsync(await seed.AddRemittanceAsync(100_000m), 30_000m);
        var s1 = await seed.AddShipmentAsync(30_000m);
        await Service(db).ReconcileAsync(ret.Key, [s1.Key], "tester");

        await new PurchaseInvoicesDeleteService(db).ExecuteAsync(ret.Key);

        Assert.False(await db.Context.StorageReturnReconciliations.AnyAsync());
    }

    /// <summary>InMemory esconde LINQ intraduzível: as consultas precisam virar SQL no provedor real.</summary>
    [Fact]
    public void Queries_translate_to_sql_server()
    {
        using var ctx = new SiagroB1.Infra.Context.AppDbContext(new DbContextOptionsBuilder<SiagroB1.Infra.Context.AppDbContext>()
            .UseSqlServer("Server=model-inspection-only;Database=none").Options);

        Assert.NotEmpty(StorageReturnReconciliationsService.CandidatesQuery(ctx, Guid.NewGuid(), ReleaseOrigin.Standard, Guid.NewGuid()).ToQueryString());
        Assert.NotEmpty(StorageReturnReconciliationsService.CandidatesQuery(ctx, Guid.NewGuid(), ReleaseOrigin.OwnershipTransfer, Guid.NewGuid()).ToQueryString());
        Assert.NotEmpty(StorageReturnReconciliationsService.ReleasesOfQuery(ctx, Guid.NewGuid()).ToQueryString());
    }
}
