using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.SalesContracts;
using SiagroB1.Application.Services.SalesInvoices;
using SiagroB1.Application.Services.SalesShipmentReleases;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Application.Services.ShipmentReleases;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.StorageDeposits;

public class StorageDepositCrossGuardsTests
{
    private static ShipmentReleasesCancelationService CancelRelease(UnitOfWork db) =>
        new(db.Context, new ShipmentReleasesRecalculateShippedService(db.Context),
            NullLogger<ShipmentReleasesCancelationService>.Instance);

    private static ShipmentReleasesDeleteService DeleteRelease(UnitOfWork db) =>
        new(db, NullLogger<ShipmentReleasesDeleteService>.Instance);

    private static SalesInvoicesCancelService CancelSales(UnitOfWork db) =>
        new(db,
            new SalesShipmentReleasesRecalculateShippedService(db.Context),
            new SalesContractsAllocationDeleteForInvoiceService(db),
            new ShipmentLoadsBalanceHookService(db.Context, new ShipmentLoadsMovementLogService(db.Context)),
            NullLogger<SalesInvoicesCancelService>.Instance);

    private static SalesInvoicesReverseConfirmService ReverseSales(UnitOfWork db) =>
        new(db, null!, null!, null!, null!, new FakeStringLocalizer<Resource>());

    [Fact]
    public async Task Release_with_active_remittance_cannot_be_cancelled()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageDepositSeed.CreateAsync(db);
        await seed.AddRemittanceAsync(100_000m, InvoiceStatus.Pending);

        var e = await Assert.ThrowsAsync<ApplicationException>(() =>
            CancelRelease(db).ExecuteAsync(seed.Release.Key, "tester", "troca de armazém"));

        Assert.StartsWith("A liberação tem a remessa para depósito nº", e.Message);
    }

    [Fact]
    public async Task Release_with_active_remittance_cannot_be_deleted()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageDepositSeed.CreateAsync(db);
        seed.Release.Status = ReleaseStatus.Pending;
        await db.SaveChangesAsync();
        await seed.AddRemittanceAsync(100_000m, InvoiceStatus.Pending);

        await Assert.ThrowsAnyAsync<Exception>(() => DeleteRelease(db).ExecuteAsync(seed.Release.Key));
        Assert.True(await db.Context.ShipmentReleases.AnyAsync(r => r.Key == seed.Release.Key));
    }

    [Fact]
    public async Task Release_with_cancelled_remittance_cannot_be_deleted_either()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageDepositSeed.CreateAsync(db);
        seed.Release.Status = ReleaseStatus.Pending;
        await db.SaveChangesAsync();
        await seed.AddRemittanceAsync(100_000m, InvoiceStatus.Cancelled);

        var e = await Assert.ThrowsAsync<ApplicationException>(() => DeleteRelease(db).ExecuteAsync(seed.Release.Key));

        Assert.Equal("A liberação tem remessa para depósito emitida e não pode ser excluída. Cancele a liberação.", e.Message);
    }

    [Fact]
    public async Task Release_with_cancelled_remittance_can_be_cancelled()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageDepositSeed.CreateAsync(db);
        await seed.AddRemittanceAsync(100_000m, InvoiceStatus.Cancelled);

        await CancelRelease(db).ExecuteAsync(seed.Release.Key, "tester", "troca de armazém");
    }

    [Fact]
    public async Task Remittance_with_return_cannot_be_cancelled()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageDepositSeed.CreateAsync(db);
        var remittance = await seed.AddRemittanceAsync(100_000m, InvoiceStatus.Pending);
        await seed.AddReturnAsync(remittance, 10_000m);

        var e = await Assert.ThrowsAsync<DefaultException>(() => CancelSales(db).ExecuteAsync(remittance.Key, "tester"));

        Assert.Equal("A remessa para depósito tem retorno lançado (nº 000000456). Cancele os retornos antes.", e.Message);
    }

    [Fact]
    public async Task Remittance_with_cancelled_return_can_be_cancelled()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageDepositSeed.CreateAsync(db);
        var remittance = await seed.AddRemittanceAsync(100_000m, InvoiceStatus.Pending);
        await seed.AddReturnAsync(remittance, 10_000m, InvoiceStatus.Cancelled);

        await CancelSales(db).ExecuteAsync(remittance.Key, "tester");
    }

    [Fact]
    public async Task Remittance_with_return_cannot_have_confirmation_reversed()
    {
        var db = TestDb.CreateUnitOfWork();
        var seed = await StorageDepositSeed.CreateAsync(db);
        var remittance = await seed.AddRemittanceAsync(100_000m, InvoiceStatus.Confirmed);
        await seed.AddReturnAsync(remittance, 10_000m);

        var e = await Assert.ThrowsAsync<DefaultException>(() => ReverseSales(db).ExecuteAsync(remittance.Key, "tester"));

        Assert.StartsWith("A remessa para depósito tem retorno lançado", e.Message);
    }
}
