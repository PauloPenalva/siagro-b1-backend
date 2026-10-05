using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services;
using SiagroB1.Application.Services.SalesContracts;
using SiagroB1.Application.Services.SalesInvoices;
using SiagroB1.Application.Services.SalesShipmentReleases;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.SalesInvoices;

/// <summary>
/// Confirmação REAL (spec §12) da devolução própria com a NF-e autorizada: o que a confirmação
/// grava na venda de origem.
/// </summary>
public class SalesInvoicesNfeReturnConfirmIntegrationTests
{
    private static SalesInvoicesConfirmService Confirm(UnitOfWork db)
    {
        var usages = new UsageService(db, NullLogger<UsageService>.Instance);

        return new SalesInvoicesConfirmService(db,
            new SalesShipmentReleasesRecalculateShippedService(db.Context),
            new SalesContractsAllocationCreateService(db, new SalesContractsFixedVolumeService(db.Context)),
            new SalesContractsAllocationCreateForReturnService(db, new SalesContractsFixedVolumeService(db.Context)),
            new SalesInvoicesUsageGuardService(usages),
            new SalesContractsAllocationCreateForFiscalAdjustmentService(db, new SalesContractsFixedVolumeService(db.Context)),
            new ShipmentLoadsBalanceHookService(db.Context, new ShipmentLoadsMovementLogService(db.Context)),
            new ShipmentLoadsClosureHookService(db.Context, new ShipmentLoadsChangeLogService(db.Context)),
            new FakeStringLocalizer<Resource>(),
            TaxTestServices.Gate(db, "STANDALONE"));
    }

    private static async Task<(NfeReturnScenario S, Guid ReturnKey)> AuthorizedReturnAsync(decimal quantity)
    {
        var s = await NfeReturnTestSeed.SeedAsync();
        var created = await NfeReturnTestSeed.CreateReturnAsync(s, quantity);
        (await s.Sale.Db.Context.SalesInvoices.SingleAsync(i => i.Key == created.Key)).NfeStatus = NfeStatus.Authorized;
        await s.Sale.Db.SaveChangesAsync();

        return (s, created.Key);
    }

    [Fact]
    public async Task Confirming_a_partial_own_return_updates_the_returned_quantity_of_the_sale_item()
    {
        var (s, returnKey) = await AuthorizedReturnAsync(10000m);

        await Confirm(s.Sale.Db).ExecuteAsync(returnKey, "tester");

        var db = s.Sale.Db.Context;
        Assert.Equal(InvoiceStatus.Confirmed, (await db.SalesInvoices.AsNoTracking().SingleAsync(i => i.Key == returnKey)).InvoiceStatus);
        Assert.Equal(10000m, (await db.SalesInvoicesItems.AsNoTracking().SingleAsync(i => i.Key == s.SaleItemKey)).ReturnedQuantity);
        Assert.Equal(InvoiceStatus.Confirmed, (await db.SalesInvoices.AsNoTracking().SingleAsync(i => i.Key == s.Sale.InvoiceKey)).InvoiceStatus);
    }

    [Fact]
    public async Task Confirming_a_total_own_return_marks_the_sale_as_returned()
    {
        var (s, returnKey) = await AuthorizedReturnAsync(30000m);

        await Confirm(s.Sale.Db).ExecuteAsync(returnKey, "tester");

        var db = s.Sale.Db.Context;
        Assert.Equal(30000m, (await db.SalesInvoicesItems.AsNoTracking().SingleAsync(i => i.Key == s.SaleItemKey)).ReturnedQuantity);
        Assert.Equal(InvoiceStatus.Returned, (await db.SalesInvoices.AsNoTracking().SingleAsync(i => i.Key == s.Sale.InvoiceKey)).InvoiceStatus);
    }
}
