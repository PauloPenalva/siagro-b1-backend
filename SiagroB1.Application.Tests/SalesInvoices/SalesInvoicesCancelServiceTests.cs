using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.SalesContracts;
using SiagroB1.Application.Services.SalesInvoices;
using SiagroB1.Application.Services.SalesShipmentReleases;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Tests.SalesInvoices;

/// <summary>Cancelamento do documento de saída dividido em checagem (ensaio) e efeitos (spec 2026-10-05 §7.1).</summary>
public class SalesInvoicesCancelServiceTests
{
    public static SalesInvoicesCancelService Service(UnitOfWork db) => new(db,
        new SalesShipmentReleasesRecalculateShippedService(db.Context),
        new SalesContractsAllocationDeleteForInvoiceService(db),
        new ShipmentLoadsBalanceHookService(db.Context, new ShipmentLoadsMovementLogService(db.Context)),
        NullLogger<SalesInvoicesCancelService>.Instance);

    [Fact]
    public async Task Common_cancel_records_who_and_when()
    {
        var scenario = await NfeTestSeed.SeedAsync();

        await Service(scenario.Db).ExecuteAsync(scenario.InvoiceKey, "tester");

        var saved = await scenario.Db.Context.SalesInvoices.AsNoTracking().SingleAsync();
        Assert.Equal(InvoiceStatus.Cancelled, saved.InvoiceStatus);
        Assert.Equal("tester", saved.CanceledBy);
        Assert.NotNull(saved.CanceledAt);
    }

    [Fact]
    public async Task Ensure_refuses_origin_with_an_active_nfe_return_without_changing_anything()
    {
        var scenario = await NfeReturnTestSeed.SeedAsync();
        await NfeReturnTestSeed.CreateReturnAsync(scenario, 10000m);

        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            Service(scenario.Sale.Db).EnsureCanCancelAsync(scenario.Sale.InvoiceKey));

        Assert.Equal("Documento de saída possui retorno.", ex.Message);
    }

    [Fact]
    public async Task Ensure_lets_an_authorized_nfe_return_through()
    {
        var scenario = await NfeReturnTestSeed.SeedAsync();
        var created = await NfeReturnTestSeed.CreateReturnAsync(scenario, 10000m);
        var nfeReturn = await scenario.Sale.Db.Context.SalesInvoices.SingleAsync(x => x.Key == created.Key);
        nfeReturn.InvoiceStatus = InvoiceStatus.Confirmed;
        nfeReturn.NfeStatus = NfeStatus.Authorized;
        await scenario.Sale.Db.SaveChangesAsync();

        await Service(scenario.Sale.Db).EnsureCanCancelAsync(created.Key);
    }

    [Fact]
    public async Task Cancel_after_nfe_requires_the_nfe_cancelled()
    {
        var scenario = await NfeTestSeed.SeedAsync();

        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            Service(scenario.Db).CancelAfterNfeAsync(scenario.InvoiceKey, "tester"));

        Assert.Equal("A NF-e deste documento não está cancelada na SEFAZ.", ex.Message);
    }

    [Fact]
    public async Task Cancel_after_nfe_skips_the_nfe_lock()
    {
        var scenario = await NfeTestSeed.SeedAsync();
        var invoice = await scenario.Db.Context.SalesInvoices.SingleAsync();
        invoice.NfeStatus = NfeStatus.Cancelled;
        await scenario.Db.SaveChangesAsync();

        await Service(scenario.Db).CancelAfterNfeAsync(scenario.InvoiceKey, "tester");

        Assert.Equal(InvoiceStatus.Cancelled, (await scenario.Db.Context.SalesInvoices.AsNoTracking().SingleAsync()).InvoiceStatus);
    }

    // ---------- GAC-1226: divergência do vínculo com NF do SAP ----------

    private static async Task<(SalesInvoiceDivergenceScenario Scenario, string DbName)> SeedDivergenceAsync()
    {
        var name = Guid.NewGuid().ToString();
        await using var seed = new AppDbContext(TestDb.Options(name));
        return (await SalesInvoiceDivergenceTestSeed.SeedAsync(seed), name);
    }

    [Fact]
    public async Task Cancelling_a_divergent_document_voids_the_record_and_reopens_the_load()
    {
        var (scenario, name) = await SeedDivergenceAsync();
        var db = TestDb.CreateUnitOfWork(name);

        await Service(db).ExecuteAsync(scenario.InvoiceKey, "tester");

        await using var fresh = new AppDbContext(TestDb.Options(name));
        var divergence = await fresh.SalesInvoiceDivergences.AsNoTracking().SingleAsync();
        Assert.Equal(SalesInvoiceDivergenceStatus.Voided, divergence.Status);
        Assert.Equal(SalesInvoiceDivergenceRules.CancelledReason, divergence.VoidReason);
        Assert.Equal("Documento de saída cancelado.", divergence.VoidReason);
        Assert.Equal("tester", divergence.VoidedBy);
        Assert.NotNull(divergence.VoidedAt);

        var load = await fresh.ShipmentLoads.AsNoTracking().SingleAsync(l => l.Key == scenario.LoadKey);
        Assert.Equal(0m, load.InvoiceDivergenceQuantity);
        Assert.Equal(0m, load.InvoicedQuantity);
        Assert.Equal(30_000m, load.AvailableQuantity);
        Assert.Equal(ShipmentLoadStatus.Open, load.Status);
    }

    [Fact]
    public async Task Cancel_for_unlink_uses_the_unlink_reason_and_does_not_commit()
    {
        var (scenario, name) = await SeedDivergenceAsync();
        var counting = new CountingUnitOfWork(TestDb.CreateUnitOfWork(name));
        var service = new SalesInvoicesCancelService(counting,
            new SalesShipmentReleasesRecalculateShippedService(counting.Context),
            new SalesContractsAllocationDeleteForInvoiceService(counting),
            new ShipmentLoadsBalanceHookService(counting.Context, new ShipmentLoadsMovementLogService(counting.Context)),
            NullLogger<SalesInvoicesCancelService>.Instance);

        await service.CancelForUnlinkAsync(scenario.InvoiceKey, "tester");

        Assert.Equal((0, 0, 0), (counting.Begins, counting.Commits, counting.Rollbacks));

        await using var fresh = new AppDbContext(TestDb.Options(name));
        Assert.Equal(InvoiceStatus.Cancelled,
            (await fresh.SalesInvoices.AsNoTracking().SingleAsync(i => i.Key == scenario.InvoiceKey)).InvoiceStatus);
        var divergence = await fresh.SalesInvoiceDivergences.AsNoTracking().SingleAsync();
        Assert.Equal(SalesInvoiceDivergenceStatus.Voided, divergence.Status);
        Assert.Equal(SalesInvoiceDivergenceRules.UnlinkedReason, divergence.VoidReason);
        Assert.Equal("Notas fiscais do SAP desvinculadas da carga.", divergence.VoidReason);
        Assert.Equal("tester", divergence.VoidedBy);

        var load = await fresh.ShipmentLoads.AsNoTracking().SingleAsync(l => l.Key == scenario.LoadKey);
        Assert.Equal(0m, load.InvoiceDivergenceQuantity);
        Assert.Equal(30_000m, load.AvailableQuantity);
        Assert.Equal(ShipmentLoadStatus.Open, load.Status);
    }
}
