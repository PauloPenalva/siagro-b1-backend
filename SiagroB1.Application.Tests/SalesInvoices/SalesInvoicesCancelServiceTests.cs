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
}
