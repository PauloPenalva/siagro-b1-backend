using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Services;
using static SiagroB1.Application.Tests.Support.InvoiceReportSeed;

namespace SiagroB1.Application.Tests.Reports;

public class FiscalInventoryServiceTests
{
    private static readonly DateTime Sep30 = new(2026, 9, 30);
    private static readonly DateTime Oct05 = new(2026, 10, 5);
    private static readonly DateTime Oct10 = new(2026, 10, 10);

    [Fact]
    public async Task Build_MatchesKardexClosingAtDate()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.FiscalInventoryOpeningBalances.Add(Opening(10m, 1000m, Sep30));
        db.Context.PurchaseInvoices.Add(Buy("C1", Oct05, 10m, 120m));
        db.Context.SalesInvoices.Add(Sell("V1", Oct10, 15m));
        // movimento depois da data: não entra
        db.Context.PurchaseInvoices.Add(Buy("C2", new DateTime(2026, 10, 11), 50m, 300m));
        await Save(db);

        // Oct10 inclui o movimento do próprio dia (a venda V1)
        var data = await Service(db).BuildAsync(new FiscalInventoryRequest { BranchCode = "01", Date = Oct10 });
        var kardex = await new FiscalKardexService(db).BuildAsync(new FiscalKardexRequest
        {
            BranchCode = "01", FromDate = Sep30, ToDate = Oct10,
        });

        var row = Assert.Single(data.Rows);
        var closing = Assert.Single(kardex.Items);
        Assert.Equal(closing.ClosingQuantity, row.Quantity);
        Assert.Equal(closing.ClosingValue, row.Value);
        Assert.Equal(5m, row.Quantity);
        Assert.Equal(600m, row.Value);
        Assert.Equal(120m, row.UnitCost);
        Assert.Equal(600m, data.TotalValue);
        Assert.Equal(Oct10, data.Date);
        Assert.Equal("01", data.BranchCode);
    }

    [Fact]
    public async Task Build_NegativeBalanceShowsNegativeQuantityAndNoUnitCost()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.SalesInvoices.Add(Sell("V1", Oct05, 10m, "A"));
        await Save(db);

        var layers = Assert.Single(await new FiscalKardexService(db).OpenLayersAsync("01", "A", Oct10));
        var data = await Service(db).BuildAsync(new FiscalInventoryRequest { BranchCode = "01", Date = Oct10 });

        var row = Assert.Single(data.Rows);
        Assert.Equal(layers.Layers.Sum(l => l.Quantity) - layers.Deficit, row.Quantity);
        Assert.True(row.Quantity < 0m);
        Assert.Null(row.UnitCost);
        Assert.Equal(-10m, row.Quantity);
        Assert.Equal(0m, row.Value);
    }

    [Fact]
    public async Task Build_ExcludesZeroItems()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.FiscalInventoryOpeningBalances.Add(Opening(10m, 1000m, Sep30, "ZERO"));
        db.Context.SalesInvoices.Add(Sell("V1", Oct05, 10m, "ZERO"));
        db.Context.FiscalInventoryOpeningBalances.Add(Opening(4m, 400m, Sep30, "VIVO"));
        await Save(db);

        var data = await Service(db).BuildAsync(new FiscalInventoryRequest { BranchCode = "01", Date = Oct10 });

        Assert.Equal(["VIVO"], data.Rows.Select(r => r.ItemCode));
    }

    [Fact]
    public async Task Build_OrdersByNcmThenItemWithBlankNcmLast()
    {
        var db = TestDb.CreateUnitOfWork();
        foreach (var (code, ncm) in new (string, string?)[] { ("B", "10019900"), ("A", "10019900"), ("C", "12019000"), ("D", null), ("E", "") })
        {
            db.Context.Items.Add(new Item { ItemCode = code, ItemName = "PRODUTO " + code, Ncm = ncm });
            db.Context.FiscalInventoryOpeningBalances.Add(Opening(1m, 10m, Sep30, code));
        }
        await Save(db);

        var data = await Service(db).BuildAsync(new FiscalInventoryRequest { BranchCode = "01", Date = Oct10 });

        Assert.Equal(["A", "B", "C", "D", "E"], data.Rows.Select(r => r.ItemCode));
        Assert.Equal(["10019900", "10019900", "12019000", "", ""], data.Rows.Select(r => r.Ncm));
    }

    [Fact]
    public async Task Build_ListsPendingComplementItems()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.FiscalInventoryOpeningBalances.Add(Opening(10m, 1000m, Sep30, "P"));
        db.Context.SalesInvoices.Add(Sell("V1", Oct05, 10m, "P"));
        var complement = Buy("CC", Oct10, 1m, 50m, "P");
        complement.InvoiceType = PurchaseInvoiceType.ValueComplement;
        complement.Items.First().ComplementedItemKey = null;
        db.Context.PurchaseInvoices.Add(complement);
        await Save(db);

        var layers = Assert.Single(await new FiscalKardexService(db).OpenLayersAsync("01", "P", Oct10));
        Assert.True(layers.PendingValue > 0m, "pré-condição: complemento sem camada aberta");

        var data = await Service(db).BuildAsync(new FiscalInventoryRequest { BranchCode = "01", Date = Oct10 });

        Assert.Equal(["P - SOJA EM GRÃOS"], data.PendingComplementItems);
    }

    [Fact]
    public async Task Build_ItemFilterRestricts()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.FiscalInventoryOpeningBalances.Add(Opening(1m, 10m, Sep30, "A"));
        db.Context.FiscalInventoryOpeningBalances.Add(Opening(2m, 20m, Sep30, "B"));
        await Save(db);

        var data = await Service(db).BuildAsync(new FiscalInventoryRequest { BranchCode = "01", Date = Oct10, ItemCode = "B" });

        var row = Assert.Single(data.Rows);
        Assert.Equal("B", row.ItemCode);
        Assert.Equal(20m, data.TotalValue);
    }

    private static FiscalInventoryOpeningBalance Opening(decimal qty, decimal value, DateTime date, string itemCode = "10001") => new()
    {
        Key = Guid.NewGuid(), BranchCode = "01", ItemCode = itemCode, ItemName = "SOJA EM GRÃOS",
        BalanceDate = date, Quantity = qty, TotalValue = value, UnitOfMeasureCode = "TN",
    };

    private static PurchaseInvoice Buy(string number, DateTime date, decimal qty, decimal unitPrice, string itemCode = "10001")
    {
        var item = PurchaseItem(quantity: qty, unitPrice: unitPrice);
        item.Cfop = "1101";
        item.ItemCode = itemCode;
        item.MovesFiscalInventory = true;
        return Purchase(number, date, branchCode: "01", items: [item]);
    }

    private static SalesInvoice Sell(string number, DateTime date, decimal qty, string itemCode = "10001")
    {
        var item = SaleItem(quantity: qty, unitPrice: 200m);
        item.Cfop = "5101";
        item.ItemCode = itemCode;
        item.MovesFiscalInventory = true;
        var sale = Sale(number, date, items: item);
        sale.NfeStatus = NfeStatus.Authorized;
        return sale;
    }

    private static FiscalInventoryService Service(IUnitOfWork db) => new(db, new FiscalKardexService(db));

    private static async Task Save(IUnitOfWork db)
    {
        await db.Context.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();
    }
}
