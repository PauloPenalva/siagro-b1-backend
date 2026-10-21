using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.SalesInvoices;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.FiscalStock;

/// <summary>
/// Saída confirmada não congelada (SAPB1): alterar data/filial ou a quantidade de linha que movimenta o estoque fiscal
/// não pode tirar nem pôr estoque num mês fechado.
/// </summary>
public class SalesInvoiceEditClosedPeriodTests
{
    private const string ClosedMessage =
        "O estoque fiscal de 09/2026 da filial 01 está fechado. Use uma data de entrada no estoque a partir de 01/10/2026 ou peça a reabertura do mês.";

    private static async Task<(UnitOfWork Db, SalesInvoice Invoice)> SeedAsync(DateTime invoiceDate)
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.Branchs.Add(new Branch { Code = "01", BranchName = "MATRIZ", StateCode = "SP", IssuesNfe = true });
        db.Context.FiscalStockPeriods.Add(new FiscalStockPeriod
            { BranchCode = "01", Year = 2026, Month = 9, Status = FiscalStockPeriodStatus.Closed });
        var invoice = new SalesInvoice
        {
            Key = Guid.NewGuid(), CardCode = "C1", BranchCode = "01", InvoiceStatus = InvoiceStatus.Confirmed,
            InvoiceType = SalesInvoiceType.Normal, NfeStatus = NfeStatus.None, InvoiceDate = invoiceDate,
            Items =
            [
                new SalesInvoiceItem
                {
                    Key = Guid.NewGuid(), ItemCode = "SOJA", UnitOfMeasureCode = "KG", Quantity = 10m, UnitPrice = 2m,
                    MovesFiscalInventory = true,
                },
            ],
        };
        db.Context.SalesInvoices.Add(invoice);
        await db.SaveChangesAsync();
        return (db, invoice);
    }

    private static FakeBusinessPartnerService Partners() =>
        new(names: new() { ["C1"] = "CLIENTE" }, states: new() { ["C1"] = "SP" });

    private static SalesInvoicesTaxApplyService Apply(UnitOfWork db) => TaxTestServices.Apply(db, Partners(), "SAPB1");

    private static SalesInvoicesUpdateService HeaderUpdate(UnitOfWork db) =>
        new(db, Partners(), Apply(db), TaxTestServices.InactiveFiscalComplement(db), NullLogger<SalesInvoicesUpdateService>.Instance);

    private static SalesInvoicesItemsUpdateService ItemUpdate(UnitOfWork db) =>
        new(db, new FakeItemService(new Dictionary<string, string> { ["SOJA"] = "SOJA" }),
            new ShipmentLoadsClosureHookService(db.Context, new ShipmentLoadsChangeLogService(db.Context)),
            Apply(db), TaxTestServices.InactiveFiscalComplement(db), PurchaseDischargeTestServices.Sync(db), NullLogger<SalesInvoicesUpdateService>.Instance);

    [Fact]
    public async Task Update_invoice_date_of_confirmed_sale_out_of_closed_month_is_refused()
    {
        var (db, invoice) = await SeedAsync(new DateTime(2026, 9, 20));
        invoice.InvoiceDate = new DateTime(2026, 10, 5);

        var ex = await Assert.ThrowsAsync<DefaultException>(() => HeaderUpdate(db).ExecuteAsync(invoice.Key, invoice, "tester"));

        Assert.Equal(ClosedMessage, ex.Message);
    }

    [Fact]
    public async Task Update_invoice_date_of_confirmed_sale_into_closed_month_is_refused()
    {
        var (db, invoice) = await SeedAsync(new DateTime(2026, 10, 5));
        invoice.InvoiceDate = new DateTime(2026, 9, 20);

        var ex = await Assert.ThrowsAsync<DefaultException>(() => HeaderUpdate(db).ExecuteAsync(invoice.Key, invoice, "tester"));

        Assert.Equal(ClosedMessage, ex.Message);
    }

    [Fact]
    public async Task Update_invoice_date_of_confirmed_sale_within_open_months_passes()
    {
        var (db, invoice) = await SeedAsync(new DateTime(2026, 10, 5));
        invoice.InvoiceDate = new DateTime(2026, 10, 6);

        await HeaderUpdate(db).ExecuteAsync(invoice.Key, invoice, "tester");

        Assert.Equal(new DateTime(2026, 10, 6), (await db.Context.SalesInvoices.AsNoTracking().SingleAsync()).InvoiceDate);
    }

    [Fact]
    public async Task Items_update_changing_quantity_on_confirmed_sale_in_closed_month_is_refused()
    {
        var (db, _) = await SeedAsync(new DateTime(2026, 9, 20));
        var item = await db.Context.SalesInvoicesItems.SingleAsync();
        item.Quantity = 20m;

        var ex = await Assert.ThrowsAsync<DefaultException>(() => ItemUpdate(db).ExecuteAsync(item.Key!.Value, item, "tester"));

        Assert.Equal(ClosedMessage, ex.Message);
    }

    [Fact]
    public async Task Items_update_changing_quantity_on_confirmed_sale_in_open_month_passes()
    {
        var (db, _) = await SeedAsync(new DateTime(2026, 10, 5));
        var item = await db.Context.SalesInvoicesItems.SingleAsync();
        item.Quantity = 20m;

        await ItemUpdate(db).ExecuteAsync(item.Key!.Value, item, "tester");

        Assert.Equal(20m, (await db.Context.SalesInvoicesItems.AsNoTracking().SingleAsync()).Quantity);
    }
}
