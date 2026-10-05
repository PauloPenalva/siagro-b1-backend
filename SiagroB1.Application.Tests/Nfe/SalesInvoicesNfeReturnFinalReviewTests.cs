using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services;
using SiagroB1.Application.Services.Nfe;
using SiagroB1.Application.Services.SalesContracts;
using SiagroB1.Application.Services.SalesInvoices;
using SiagroB1.Application.Services.SalesShipmentReleases;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Application.Services.ShipmentReleases;
using SiagroB1.Application.Services.Taxes;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.Nfe;

/// <summary>Correções da revisão final da devolução própria (B1-B6).</summary>
public class SalesInvoicesNfeReturnFinalReviewTests
{
    private static SalesInvoicesCancelService Cancel(UnitOfWork db) => new(db,
        new SalesShipmentReleasesRecalculateShippedService(db.Context),
        new SalesContractsAllocationDeleteForInvoiceService(db),
        new ShipmentLoadsBalanceHookService(db.Context, new ShipmentLoadsMovementLogService(db.Context)),
        NullLogger<SalesInvoicesCancelService>.Instance);

    private static SalesInvoicesDeleteService Delete(UnitOfWork db) => new(db,
        new ShipmentLoadsBalanceHookService(db.Context, new ShipmentLoadsMovementLogService(db.Context)),
        NullLogger<SalesInvoicesDeleteService>.Instance);

    private static SalesInvoicesReverseConfirmService ReverseConfirm(UnitOfWork db) => new(db,
        new SalesContractsAllocationDeleteForInvoiceService(db),
        new ShipmentReleasesRecalculateShippedService(db.Context),
        new ShipmentLoadsBalanceHookService(db.Context, new ShipmentLoadsMovementLogService(db.Context)),
        new ShipmentLoadsClosureHookService(db.Context, new ShipmentLoadsChangeLogService(db.Context)),
        new FakeStringLocalizer<Resource>());

    private static SalesInvoicesItemsUpdateService ItemUpdate(UnitOfWork db)
    {
        var partners = new FakeBusinessPartnerService(
            names: new() { [NfeTestSeed.CardCode] = "CLIENTE BA LTDA" }, states: new() { [NfeTestSeed.CardCode] = "BA" });

        return new(db, new FakeItemService(new Dictionary<string, string> { ["SOJA"] = "SOJA EM GRAOS" }),
            new ShipmentLoadsClosureHookService(db.Context, new ShipmentLoadsChangeLogService(db.Context)),
            TaxTestServices.Apply(db, partners), NullLogger<SalesInvoicesUpdateService>.Instance);
    }

    private static SalesInvoicesNfeIssueService Issue(NfeScenario scenario)
    {
        var config = NfeTestSeed.Config();
        var options = new NfeOptions(config);
        var sefaz = new FakeNfeSefazClient();

        return new SalesInvoicesNfeIssueService(
            scenario.Db, new TaxCalculationGate(scenario.Db, config), new NfeReadinessValidator(scenario.Db, options),
            new BranchNfeSettingsService(scenario.Db, options, sefaz), new FakeNfeNumberReservationService(), sefaz,
            new SalesInvoiceNfeResultHandler(scenario.Db, new RecordingConfirmService(scenario.Db), NullLogger<SalesInvoiceNfeResultHandler>.Instance),
            options, NullLogger<SalesInvoicesNfeIssueService>.Instance, NfeTestSeed.Clock, NfeIssueInputAssembler.BrasiliaZone);
    }

    /// <summary>Fecha a entrega da venda (cabeçalho e linha), como a conferência de entrega deixaria.</summary>
    private static async Task CloseSaleDeliveryAsync(NfeReturnScenario s)
    {
        var sale = await s.Sale.Db.Context.SalesInvoices.Include(i => i.Items).SingleAsync(i => i.Key == s.Sale.InvoiceKey);
        sale.DeliveryStatus = SalesInvoiceDeliveryStatus.Closed;
        foreach (var item in sale.Items)
        {
            item.DeliveryStatus = SalesInvoiceDeliveryStatus.Closed;
            item.DeliveredQuantity = item.Quantity;
        }

        await s.Sale.Db.SaveChangesAsync();
    }

    private static async Task AssertSaleDeliveryUntouchedAsync(NfeReturnScenario s, InvoiceStatus expectedStatus)
    {
        var sale = await s.Sale.Db.Context.SalesInvoices.AsNoTracking().Include(i => i.Items)
            .SingleAsync(i => i.Key == s.Sale.InvoiceKey);
        Assert.Equal(expectedStatus, sale.InvoiceStatus);
        Assert.Equal(SalesInvoiceDeliveryStatus.Closed, sale.DeliveryStatus);
        var item = sale.Items.Single();
        Assert.Equal(SalesInvoiceDeliveryStatus.Closed, item.DeliveryStatus);
        Assert.Equal(item.Quantity, item.DeliveredQuantity);
    }

    [Fact]
    public async Task B1_cancelling_a_pending_own_return_leaves_the_sale_delivery_untouched()
    {
        var s = await NfeReturnTestSeed.SeedAsync();
        await CloseSaleDeliveryAsync(s);
        var created = await NfeReturnTestSeed.CreateReturnAsync(s, 10000m);

        await Cancel(s.Sale.Db).ExecuteAsync(created.Key, "tester");

        await AssertSaleDeliveryUntouchedAsync(s, InvoiceStatus.Confirmed);
    }

    [Fact]
    public async Task B1_cancelling_an_own_return_moves_a_returned_sale_back_to_confirmed_without_reopening_delivery()
    {
        var s = await NfeReturnTestSeed.SeedAsync();
        await CloseSaleDeliveryAsync(s);
        var created = await NfeReturnTestSeed.CreateReturnAsync(s, 30000m);
        (await s.Sale.Db.Context.SalesInvoices.SingleAsync(i => i.Key == s.Sale.InvoiceKey)).InvoiceStatus = InvoiceStatus.Returned;
        await s.Sale.Db.SaveChangesAsync();

        await Cancel(s.Sale.Db).ExecuteAsync(created.Key, "tester");

        await AssertSaleDeliveryUntouchedAsync(s, InvoiceStatus.Confirmed);
    }

    [Fact]
    public async Task B1_deleting_a_pending_own_return_leaves_the_sale_delivery_untouched()
    {
        var s = await NfeReturnTestSeed.SeedAsync();
        await CloseSaleDeliveryAsync(s);
        var created = await NfeReturnTestSeed.CreateReturnAsync(s, 10000m);

        await Delete(s.Sale.Db).ExecuteAsync(created.Key, "tester");

        await AssertSaleDeliveryUntouchedAsync(s, InvoiceStatus.Confirmed);
    }

    [Fact]
    public async Task B1_deleting_an_own_return_moves_a_returned_sale_back_to_confirmed_without_reopening_delivery()
    {
        var s = await NfeReturnTestSeed.SeedAsync();
        await CloseSaleDeliveryAsync(s);
        var created = await NfeReturnTestSeed.CreateReturnAsync(s, 30000m);
        (await s.Sale.Db.Context.SalesInvoices.SingleAsync(i => i.Key == s.Sale.InvoiceKey)).InvoiceStatus = InvoiceStatus.Returned;
        await s.Sale.Db.SaveChangesAsync();

        await Delete(s.Sale.Db).ExecuteAsync(created.Key, "tester");

        await AssertSaleDeliveryUntouchedAsync(s, InvoiceStatus.Confirmed);
    }

    [Fact]
    public async Task B2_reverse_confirm_of_an_own_return_is_refused_and_touches_no_romaneio()
    {
        var s = await NfeReturnTestSeed.SeedAsync();
        var created = await NfeReturnTestSeed.CreateReturnAsync(s, 10000m);
        var context = s.Sale.Db.Context;
        (await context.SalesInvoices.SingleAsync(i => i.Key == created.Key)).InvoiceStatus = InvoiceStatus.Confirmed;
        var orphan = new StorageTransaction
        {
            Key = Guid.NewGuid(), CardCode = NfeTestSeed.CardCode, ItemCode = "SOJA", UnitOfMeasureCode = "KG",
            WarehouseCode = "01", TransactionType = StorageTransactionType.SalesShipment,
            TransactionStatus = StorageTransactionsStatus.Confirmed, NetWeight = 10000m, GrossWeight = 10000m,
        };
        context.StorageTransactions.Add(orphan);
        await s.Sale.Db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<DefaultException>(() => ReverseConfirm(s.Sale.Db).ExecuteAsync(created.Key, "tester"));

        Assert.Equal(
            "A devolução com NF-e não pode ser estornada: ela só sai pelo cancelamento da NF-e de devolução, recurso da próxima etapa.",
            ex.Message);
        var saved = await context.StorageTransactions.AsNoTracking().SingleAsync(t => t.Key == orphan.Key);
        Assert.Null(saved.SalesInvoiceKey);
        Assert.Equal(StorageTransactionsStatus.Confirmed, saved.TransactionStatus);
    }

    [Fact]
    public async Task B3_sold_plus_one_thousandth_is_refused_at_creation()
    {
        var s = await NfeReturnTestSeed.SeedAsync();

        var ex = await Assert.ThrowsAsync<DefaultException>(() => NfeReturnTestSeed.CreateReturnAsync(s, 30000.001m));

        Assert.Contains("passa do saldo devolvível", ex.Message);
    }

    [Fact]
    public async Task B3_sold_plus_one_thousandth_is_refused_at_line_edit()
    {
        var s = await NfeReturnTestSeed.SeedAsync();
        var created = await NfeReturnTestSeed.CreateReturnAsync(s, 30000m);
        var line = await s.Sale.Db.Context.SalesInvoicesItems.SingleAsync(i => i.SalesInvoiceKey == created.Key);
        line.Quantity = 30000.001m;

        var ex = await Assert.ThrowsAsync<DefaultException>(() => ItemUpdate(s.Sale.Db).ExecuteAsync(line.Key!.Value, line, "tester"));

        Assert.Contains("passa do saldo devolvível da venda", ex.Message);
    }

    [Fact]
    public async Task B3_sold_plus_one_thousandth_is_refused_at_issue()
    {
        var s = await NfeReturnTestSeed.SeedAsync();
        var created = await NfeReturnTestSeed.CreateReturnAsync(s, 30000m);
        (await s.Sale.Db.Context.SalesInvoicesItems.SingleAsync(i => i.Key == s.SaleItemKey)).Quantity = 29999.999m;
        await s.Sale.Db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<DefaultException>(() => Issue(s.Sale).ExecuteAsync(created.Key, "tester"));

        Assert.Contains("passa do saldo devolvível da venda", ex.Message);
        Assert.Null((await s.Sale.Db.Context.SalesInvoices.AsNoTracking().SingleAsync(i => i.Key == created.Key)).TaxDocumentNumber);
    }

    [Fact]
    public async Task B4_item_update_never_changes_the_stored_nfe_item_number()
    {
        var s = await NfeReturnTestSeed.SeedAsync();
        var line = await s.Sale.Db.Context.SalesInvoicesItems.SingleAsync(i => i.Key == s.SaleItemKey);
        Assert.Equal(1, line.NfeItemNumber);
        line.NfeItemNumber = 7;

        await ItemUpdate(s.Sale.Db).ExecuteAsync(line.Key!.Value, line, "tester");

        var saved = await s.Sale.Db.Context.SalesInvoicesItems.AsNoTracking().SingleAsync(i => i.Key == s.SaleItemKey);
        Assert.Equal(1, saved.NfeItemNumber);
    }

    [Fact]
    public async Task B5_own_return_does_not_carry_the_sale_delivery_location()
    {
        var s = await NfeReturnTestSeed.SeedAsync();
        var sale = await s.Sale.Db.Context.SalesInvoices.SingleAsync(i => i.Key == s.Sale.InvoiceKey);
        sale.DeliveryCardCode = "ENTREGA1";
        sale.DeliveryCardName = "LOCAL DE ENTREGA";
        await s.Sale.Db.SaveChangesAsync();

        var created = await NfeReturnTestSeed.CreateReturnAsync(s, 10000m);

        var saved = await s.Sale.Db.Context.SalesInvoices.AsNoTracking().SingleAsync(i => i.Key == created.Key);
        Assert.Null(saved.DeliveryCardCode);
        // O create service grava "" quando não há código (comportamento comum a todo documento).
        Assert.True(string.IsNullOrEmpty(saved.DeliveryCardName));
    }

    [Fact]
    public async Task B6_header_weight_disagreeing_with_the_items_is_refused_before_reserving_a_number()
    {
        var s = await NfeReturnTestSeed.SeedAsync();
        var created = await NfeReturnTestSeed.CreateReturnAsync(s, 10000m);
        (await s.Sale.Db.Context.SalesInvoices.SingleAsync(i => i.Key == created.Key)).NetWeight = 5m;
        await s.Sale.Db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => Issue(s.Sale).ExecuteAsync(created.Key, "tester"));

        Assert.Contains("não confere com a soma das quantidades", ex.Message);
        Assert.Null((await s.Sale.Db.Context.SalesInvoices.AsNoTracking().SingleAsync(i => i.Key == created.Key)).TaxDocumentNumber);
    }
}
