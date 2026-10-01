using Microsoft.EntityFrameworkCore;
using Microsoft.OData.Edm;
using Microsoft.OData.ModelBuilder;
using SiagroB1.Application.Services.SalesContracts;
using SiagroB1.Application.Services.SalesInvoices;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Application.Services.ShipmentReleases;
using SiagroB1.Application.Tests.SalesContracts;
using SiagroB1.Application.Tests.ShipmentLoads;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using SiagroB1.Web.ODataConfig;

namespace SiagroB1.Application.Tests.SalesInvoices;

/// <summary>
/// GAC-1171 (rateio): quanto de cada linha já voltou em devolução CONFIRMADA. É a base do rateio do
/// ticket de descarga e o selo "Dev. parcial" das telas — sem mudar o status da nota.
/// </summary>
public class SalesInvoicesReturnedQuantityTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    /// <summary>Recusa de carga pelo caminho REAL, com faturamento de verdade antes.</summary>
    private readonly ShipmentLoadsRefuseServiceTests _refusal = new();

    private static SalesInvoice Return(
        SalesInvoice origin, InvoiceStatus status, params (SalesInvoiceItem Origin, decimal Quantity)[] lines)
    {
        var invoice = SalesContractsAllocationTestSupport.NewInvoice(
            status, SalesInvoiceType.Return, originKey: origin.Key);

        foreach (var (originItem, quantity) in lines)
            SalesContractsAllocationTestSupport.NewItem(
                invoice, contractKey: null, releaseKey: null, quantity: quantity, originItemKey: originItem.Key);

        return invoice;
    }

    private static SalesInvoicesReverseConfirmService Reverse(UnitOfWork db) =>
        new(db,
            new SalesContractsAllocationDeleteForInvoiceService(db),
            new ShipmentReleasesRecalculateShippedService(db.Context),
            new ShipmentLoadsBalanceHookService(db.Context, new ShipmentLoadsMovementLogService(db.Context)),
            new ShipmentLoadsClosureHookService(db.Context, new ShipmentLoadsChangeLogService(db.Context)),
            new FakeStringLocalizer<Resource>());

    private static Task<SalesInvoice> OriginAsync(UnitOfWork db, Guid key) =>
        db.Context.SalesInvoices.AsNoTracking().Include(x => x.Items).SingleAsync(x => x.Key == key);

    [Fact]
    public async Task Only_confirmed_returns_count_on_each_line_and_on_the_header()
    {
        var origin = SalesContractsAllocationTestSupport.NewInvoice();
        var soy = SalesContractsAllocationTestSupport.NewItem(origin, null, null, 40_000m);
        var corn = SalesContractsAllocationTestSupport.NewItem(origin, null, null, 10_000m, itemCode: "MILHO");

        _db.Context.SalesInvoices.AddRange(
            origin,
            Return(origin, InvoiceStatus.Confirmed, (soy, 5_000m), (corn, 1_000m)),
            Return(origin, InvoiceStatus.Confirmed, (soy, 2_500m)),
            Return(origin, InvoiceStatus.Pending, (soy, 9_000m)),
            Return(origin, InvoiceStatus.Cancelled, (corn, 3_000m)));
        await _db.Context.SaveChangesAsync();

        await SalesInvoicesRecalculateReturnedService.RecalculateAsync(_db.Context, origin.Key);

        Assert.Equal(7_500m, soy.ReturnedQuantity);
        Assert.Equal(1_000m, corn.ReturnedQuantity);
        Assert.Equal(8_500m, origin.ReturnedQuantity);
        Assert.Equal(InvoiceStatus.Confirmed, origin.InvoiceStatus);
    }

    /// <summary>
    /// Quem chama acabou de trocar o status da devolução e ainda não salvou. O escritor precisa ler
    /// o status RASTREADO, senão gravaria o valor anterior ao próprio ato que o chamou.
    /// </summary>
    [Fact]
    public async Task An_unsaved_status_change_of_the_return_is_seen()
    {
        var origin = SalesContractsAllocationTestSupport.NewInvoice();
        var soy = SalesContractsAllocationTestSupport.NewItem(origin, null, null, 40_000m);
        var confirming = Return(origin, InvoiceStatus.Pending, (soy, 10_000m));
        var reversing = Return(origin, InvoiceStatus.Confirmed, (soy, 4_000m));
        _db.Context.SalesInvoices.AddRange(origin, confirming, reversing);
        await _db.Context.SaveChangesAsync();

        confirming.InvoiceStatus = InvoiceStatus.Confirmed; // sem SaveChanges
        reversing.InvoiceStatus = InvoiceStatus.Pending;    // sem SaveChanges

        await SalesInvoicesRecalculateReturnedService.RecalculateAsync(_db.Context, origin.Key);

        Assert.Equal(10_000m, soy.ReturnedQuantity);
    }

    [Fact]
    public async Task A_missing_origin_is_a_no_op()
    {
        await SalesInvoicesRecalculateReturnedService.RecalculateAsync(_db.Context, null);
        await SalesInvoicesRecalculateReturnedService.RecalculateAsync(_db.Context, Guid.NewGuid());
    }

    [Fact]
    public void Remaining_quantity_is_billed_minus_returned()
    {
        var item = new SalesInvoiceItem
        {
            ItemCode = "SOJA", UnitOfMeasureCode = "KG", Quantity = 40_000m, ReturnedQuantity = 10_000.0004m,
        };

        Assert.Equal(30_000.000m, ShipmentLoadDischargeRules.RemainingQuantity(item));
    }

    [Fact]
    public async Task A_partial_refusal_marks_the_returned_quantity_and_keeps_the_invoice_confirmed()
    {
        var (load, invoice) = await _refusal.BilledLoadAsync(40_000m);

        await _refusal.Service().ExecuteAsync(
            ShipmentLoadsRefuseServiceTests.Request(load, invoice, 10_000m), "tester");

        var origin = await OriginAsync(_refusal._db, invoice.Key);
        Assert.Equal(InvoiceStatus.Confirmed, origin.InvoiceStatus);
        Assert.Equal(10_000m, origin.ReturnedQuantity);
        Assert.Equal(10_000m, Assert.Single(origin.Items).ReturnedQuantity);
    }

    [Fact]
    public async Task A_total_refusal_returns_everything_and_the_invoice_becomes_returned()
    {
        var (load, invoice) = await _refusal.BilledLoadAsync(40_000m);

        await _refusal.Service().ExecuteAsync(
            ShipmentLoadsRefuseServiceTests.Request(load, invoice, 40_000m), "tester");

        var origin = await OriginAsync(_refusal._db, invoice.Key);
        Assert.Equal(InvoiceStatus.Returned, origin.InvoiceStatus);
        Assert.Equal(40_000m, origin.ReturnedQuantity);
    }

    [Fact]
    public async Task Reversing_the_return_confirmation_zeroes_the_returned_quantity()
    {
        var (load, invoice) = await _refusal.BilledLoadAsync(40_000m);
        await _refusal.Service().ExecuteAsync(
            ShipmentLoadsRefuseServiceTests.Request(load, invoice, 10_000m), "tester");

        var returnKey = await _refusal._db.Context.SalesInvoices.AsNoTracking()
            .Where(x => x.InvoiceType == SalesInvoiceType.Return)
            .Select(x => x.Key)
            .SingleAsync();

        await Reverse(_refusal._db).ExecuteAsync(returnKey, "tester");

        var origin = await OriginAsync(_refusal._db, invoice.Key);
        Assert.Equal(0m, origin.ReturnedQuantity);
        Assert.Equal(0m, Assert.Single(origin.Items).ReturnedQuantity);
    }

    /// <summary>A lista de documentos e o grid de rateio leem as duas colunas pelo OData.</summary>
    [Fact]
    public void The_returned_quantity_is_in_the_edm_for_the_list_and_the_line()
    {
        var builder = new ODataConventionModelBuilder();
        builder.ConfigureODataEntities();
        var model = builder.GetEdmModel();

        var invoice = (IEdmStructuredType)model.FindDeclaredType(typeof(SalesInvoice).FullName);
        var item = (IEdmStructuredType)model.FindDeclaredType(typeof(SalesInvoiceItem).FullName);

        Assert.NotNull(invoice.FindProperty(nameof(SalesInvoice.ReturnedQuantity)));
        Assert.NotNull(item.FindProperty(nameof(SalesInvoiceItem.ReturnedQuantity)));
    }
}
