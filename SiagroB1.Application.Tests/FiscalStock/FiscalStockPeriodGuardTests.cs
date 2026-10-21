using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.FiscalInventory;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;
using SiagroB1.Infra.FiscalStock;

namespace SiagroB1.Application.Tests.FiscalStock;

/// <summary>Trava do mês fechado do estoque fiscal (spec 2026-10-20 §4/§5, decisão 10).</summary>
public class FiscalStockPeriodGuardTests
{
    private const string ClosedMessage =
        "O estoque fiscal de 09/2026 da filial 01 está fechado. Use uma data de entrada no estoque a partir de 01/10/2026 ou peça a reabertura do mês.";

    private static async Task CloseAsync(IUnitOfWork db, int year, int month, string branch = "01")
    {
        db.Context.FiscalStockPeriods.Add(new FiscalStockPeriod
        {
            BranchCode = branch, Year = year, Month = month, Status = FiscalStockPeriodStatus.Closed,
        });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Ensure_open_refuses_date_in_closed_month_with_first_open_date()
    {
        var db = TestDb.CreateUnitOfWork();
        await CloseAsync(db, 2026, 8);
        await CloseAsync(db, 2026, 9);
        var guard = new FiscalStockPeriodGuard(db);

        var ex = await Assert.ThrowsAsync<DefaultException>(() => guard.EnsureOpenAsync("01", new DateTime(2026, 9, 30)));
        Assert.Equal(ClosedMessage, ex.Message);

        await guard.EnsureOpenAsync("01", new DateTime(2026, 10, 1));
        await guard.EnsureOpenAsync("02", new DateTime(2026, 9, 30));
        Assert.Equal(new DateTime(2026, 10, 1), await guard.FirstOpenDateAsync("01"));
        Assert.Null(await guard.FirstOpenDateAsync("02"));
    }

    [Fact]
    public async Task Reopened_month_is_not_closed()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.FiscalStockPeriods.Add(new FiscalStockPeriod
            { BranchCode = "01", Year = 2026, Month = 9, Status = FiscalStockPeriodStatus.Open });
        await db.SaveChangesAsync();

        Assert.False(await new FiscalStockPeriodGuard(db).IsClosedAsync("01", 2026, 9));
    }

    private static async Task<(UnitOfWork Db, PurchaseInvoice Invoice)> SeedPurchaseAsync(
        DateTime issueDate, DateTime? postingDate, bool movesStock = true)
    {
        var db = TestDb.CreateUnitOfWork();
        await CloseAsync(db, 2026, 9);
        var invoice = new PurchaseInvoice
        {
            Key = Guid.NewGuid(), BranchCode = "01", CardCode = "F0001", InvoiceStatus = InvoiceStatus.Pending,
            IssueDate = issueDate, PostingDate = postingDate,
        };
        invoice.AddItem(new PurchaseInvoiceItem
        {
            Key = Guid.NewGuid(), ItemCode = "SOJA", Quantity = 10m, UnitPrice = 1m, MovesFiscalInventory = movesStock,
        });
        db.Context.PurchaseInvoices.Add(invoice);
        await db.SaveChangesAsync();
        return (db, invoice);
    }

    [Fact]
    public async Task Confirm_purchase_with_posting_date_in_closed_month_is_refused()
    {
        var (db, invoice) = await SeedPurchaseAsync(new DateTime(2026, 9, 10), new DateTime(2026, 9, 30));

        var ex = await Assert.ThrowsAsync<DefaultException>(
            () => TaxTestServices.PurchaseConfirm(db, "SAPB1").ExecuteAsync(invoice.Key, "tester"));

        Assert.Equal(ClosedMessage, ex.Message);
        Assert.Equal(InvoiceStatus.Pending, (await db.Context.PurchaseInvoices.AsNoTracking().SingleAsync()).InvoiceStatus);
    }

    [Fact]
    public async Task Confirm_purchase_issued_in_closed_month_with_posting_date_in_open_month_passes()
    {
        var (db, invoice) = await SeedPurchaseAsync(new DateTime(2026, 9, 28), new DateTime(2026, 10, 2));

        await TaxTestServices.PurchaseConfirm(db, "SAPB1").ExecuteAsync(invoice.Key, "tester");

        Assert.Equal(InvoiceStatus.Confirmed, invoice.InvoiceStatus);
    }

    [Fact]
    public async Task Confirm_purchase_that_does_not_move_stock_ignores_closed_month()
    {
        var (db, invoice) = await SeedPurchaseAsync(new DateTime(2026, 9, 10), new DateTime(2026, 9, 30), movesStock: false);

        await TaxTestServices.PurchaseConfirm(db, "SAPB1").ExecuteAsync(invoice.Key, "tester");

        Assert.Equal(InvoiceStatus.Confirmed, invoice.InvoiceStatus);
    }

    [Fact]
    public async Task Reverse_confirmed_purchase_in_closed_month_is_refused()
    {
        var (db, invoice) = await SeedPurchaseAsync(new DateTime(2026, 9, 10), null);
        invoice.InvoiceStatus = InvoiceStatus.Confirmed;
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<DefaultException>(
            () => FinancialDocumentTestServices.PurchaseReverse(db).ExecuteAsync(invoice.Key, "tester"));

        Assert.Equal(ClosedMessage, ex.Message);
    }

    [Fact]
    public async Task Cancel_sales_nfe_in_closed_month_is_refused()
    {
        var scenario = await NfeTestSeed.SeedAsync();
        await NfeCancelTestServices.AuthorizeSaleAsync(scenario.Db, scenario.InvoiceKey);
        var invoice = await scenario.Db.Context.SalesInvoices.SingleAsync(x => x.Key == scenario.InvoiceKey);
        invoice.InvoiceDate = new DateTime(2026, 9, 15);
        foreach (var line in await scenario.Db.Context.SalesInvoicesItems.Where(l => l.SalesInvoiceKey == invoice.Key).ToListAsync())
            line.MovesFiscalInventory = true;
        await scenario.Db.SaveChangesAsync();
        await CloseAsync(scenario.Db, 2026, 9);
        var sefaz = new FakeNfeSefazClient();

        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            NfeCancelTestServices.SalesCancel(scenario.Db, sefaz).ExecuteAsync(scenario.InvoiceKey, "Venda desfeita pelo cliente", "tester"));

        Assert.Equal(ClosedMessage, ex.Message);
        Assert.Empty(sefaz.CancelRequests);
    }

    [Fact]
    public async Task Opening_balance_in_closed_month_cannot_be_edited()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.Items.Add(new Item { ItemCode = "SOJA", ItemName = "SOJA EM GRAOS" });
        var balance = new FiscalInventoryOpeningBalance
        {
            Key = Guid.NewGuid(), BranchCode = "01", ItemCode = "SOJA", BalanceDate = new DateTime(2026, 9, 1),
            Quantity = 100m, TotalValue = 5000m, UnitOfMeasureCode = "SC",
        };
        db.Context.FiscalInventoryOpeningBalances.Add(balance);
        await db.SaveChangesAsync();
        await CloseAsync(db, 2026, 9);
        var service = new FiscalInventoryOpeningBalancesService(db);

        var moved = new FiscalInventoryOpeningBalance
        {
            BranchCode = "01", ItemCode = "SOJA", BalanceDate = new DateTime(2026, 10, 1),
            Quantity = 100m, TotalValue = 5000m, UnitOfMeasureCode = "SC",
        };
        var ex = await Assert.ThrowsAsync<DefaultException>(() => service.UpdateAsync(balance.Key, moved));
        Assert.Equal(ClosedMessage, ex.Message);

        ex = await Assert.ThrowsAsync<DefaultException>(() => service.DeleteAsync(balance.Key));
        Assert.Equal(ClosedMessage, ex.Message);
    }
}
