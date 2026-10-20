using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Services;
using static SiagroB1.Application.Tests.Support.InvoiceReportSeed;

namespace SiagroB1.Application.Tests.Reports;

public class FiscalKardexServiceTests
{
    private static readonly DateTime Sep30 = new(2026, 9, 30);
    private static readonly DateTime Oct05 = new(2026, 10, 5);
    private static readonly DateTime Oct10 = new(2026, 10, 10);

    [Fact]
    public async Task Build_OpeningPlusEntryAndSale()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.FiscalInventoryOpeningBalances.Add(Opening(10m, 1000m, Sep30));
        db.Context.PurchaseInvoices.Add(Buy("C1", Oct05, 10m, 120m));
        db.Context.SalesInvoices.Add(Sell("V1", Oct10, 15m));
        await Save(db);

        var item = Assert.Single((await Service(db).BuildAsync(October())).Items);

        Assert.Equal("10001", item.ItemCode);
        Assert.Equal(10m, item.OpeningQuantity);
        Assert.Equal(1000m, item.OpeningValue);
        Assert.Equal(new[] { "Entrada", "Saída" }, item.Lines.Select(l => l.Kind));
        Assert.Equal(1200m, item.Lines[0].ValueIn);
        Assert.Equal("E", item.Lines[0].Direction);
        Assert.Equal("S", item.Lines[1].Direction);
        Assert.Equal(15m, item.Lines[1].QuantityOut);
        Assert.Equal(1600m, item.Lines[1].ValueOut);
        Assert.Equal(5m, item.ClosingQuantity);
        Assert.Equal(600m, item.ClosingValue);
        Assert.Equal("V1", item.Lines[1].InternalNumber);
        Assert.Equal("5101", item.Lines[1].Cfop);
    }

    [Fact]
    public async Task Build_EntryCostExcludesIcms()
    {
        var db = TestDb.CreateUnitOfWork();
        var purchase = Buy("C1", Oct05, 10m, 100m);
        purchase.Items.First().IcmsValue = 120m;
        db.Context.PurchaseInvoices.Add(purchase);
        await Save(db);

        var line = Assert.Single(Assert.Single((await Service(db).BuildAsync(October())).Items).Lines);

        Assert.Equal(880m, line.ValueIn);
        Assert.Equal(88m, line.UnitCost);
        Assert.Equal(1000m, line.InvoiceValue);
    }

    [Fact]
    public async Task Build_IgnoresLinesNotMovingInventory()
    {
        var db = TestDb.CreateUnitOfWork();
        var purchase = Buy("C1", Oct05, 10m, 100m);
        purchase.Items.First().MovesFiscalInventory = false;
        var noItem = Buy("C2", Oct05, 10m, 100m);
        noItem.Items.First().ItemCode = null;
        db.Context.PurchaseInvoices.AddRange(purchase, noItem);
        await Save(db);

        Assert.Empty((await Service(db).BuildAsync(October())).Items);
    }

    [Fact]
    public async Task Build_IgnoresMovementsUpToOpeningDate()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.FiscalInventoryOpeningBalances.Add(Opening(10m, 1000m, Oct05));
        db.Context.PurchaseInvoices.Add(Buy("ANTES", new DateTime(2026, 10, 2), 5m, 100m));
        db.Context.PurchaseInvoices.Add(Buy("NODIA", Oct05, 5m, 100m));
        db.Context.PurchaseInvoices.Add(Buy("DEPOIS", new DateTime(2026, 10, 6), 5m, 100m));
        await Save(db);

        var item = Assert.Single((await Service(db).BuildAsync(October())).Items);

        Assert.Equal(new[] { "", "DEPOIS" }, item.Lines.Select(l => l.InternalNumber));
        Assert.Equal(15m, item.ClosingQuantity);
        Assert.Equal(1500m, item.ClosingValue);
    }

    [Fact]
    public async Task Build_FromBeforeOpeningShowsOpeningAsFirstLine()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.FiscalInventoryOpeningBalances.Add(Opening(10m, 1000m, Oct05));
        await Save(db);

        var item = Assert.Single((await Service(db).BuildAsync(October())).Items);

        Assert.Equal(0m, item.OpeningQuantity);
        Assert.Equal(0m, item.OpeningValue);
        var line = Assert.Single(item.Lines);
        Assert.Equal("Saldo inicial", line.Kind);
        Assert.Equal("E", line.Direction);
        Assert.Equal(Oct05, line.Date);
        Assert.Equal(10m, line.QuantityIn);
        Assert.Equal(1000m, line.ValueIn);
        Assert.Equal(10m, item.ClosingQuantity);
        Assert.Equal(1000m, item.ClosingValue);
    }

    [Fact]
    public async Task Build_ItemWithoutMovementInPeriodShowsBalance()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.FiscalInventoryOpeningBalances.Add(Opening(10m, 1000m, Sep30));
        await Save(db);

        var item = Assert.Single((await Service(db).BuildAsync(October())).Items);

        Assert.Empty(item.Lines);
        Assert.Equal(10m, item.OpeningQuantity);
        Assert.Equal(1000m, item.OpeningValue);
        Assert.Equal(10m, item.ClosingQuantity);
        Assert.Equal(1000m, item.ClosingValue);
    }

    [Fact]
    public async Task Build_CancelledNfeDisappears()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.PurchaseInvoices.Add(Buy("C1", Oct05, 10m, 100m));
        var sale = Sell("V1", Oct10, 4m);
        sale.NfeStatus = NfeStatus.Cancelled;
        db.Context.SalesInvoices.Add(sale);
        await Save(db);

        var item = Assert.Single((await Service(db).BuildAsync(October())).Items);

        Assert.Equal(new[] { "C1" }, item.Lines.Select(l => l.InternalNumber));
        Assert.Equal(10m, item.ClosingQuantity);
    }

    [Fact]
    public async Task Build_SalesReturnWithEntryCfopIsEntry()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.PurchaseInvoices.Add(Buy("C1", Oct05, 10m, 100m));
        var sale = Sell("V1", Oct10, 4m);
        db.Context.SalesInvoices.Add(sale);
        var returned = Sell("DV1", new DateTime(2026, 10, 12), 1m, cfop: "1202");
        returned.InvoiceType = SalesInvoiceType.Return;
        returned.Items.First().SalesInvoiceItemOriginKey = sale.Items.First().Key;
        db.Context.SalesInvoices.Add(returned);
        await Save(db);

        var item = Assert.Single((await Service(db).BuildAsync(October())).Items);

        var line = item.Lines.Single(l => l.InternalNumber == "DV1");
        Assert.Equal("Devolução de venda", line.Kind);
        Assert.Equal("E", line.Direction);
        Assert.Equal(1m, line.QuantityIn);
        Assert.Equal(100m, line.ValueIn);
        Assert.Equal(7m, item.ClosingQuantity);
        Assert.Equal(700m, item.ClosingValue);
    }

    [Fact]
    public async Task Build_PurchaseReturnLeavesFromOriginLayer()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.PurchaseInvoices.Add(Buy("C1", Oct05, 10m, 100m));
        var origin = Buy("C2", Oct05, 10m, 150m);
        db.Context.PurchaseInvoices.Add(origin);
        var returned = Buy("DC", Oct10, 2m, 150m, cfop: "5202");
        returned.InvoiceType = PurchaseInvoiceType.Return;
        // origem = a 2ª compra (camada de 150), não a mais antiga
        returned.Items.First().PurchaseInvoiceItemOriginKey = origin.Items.First().Key;
        db.Context.PurchaseInvoices.Add(returned);
        await Save(db);

        var item = Assert.Single((await Service(db).BuildAsync(October())).Items);

        var line = item.Lines.Single(l => l.InternalNumber == "DC");
        Assert.Equal("Devolução de compra", line.Kind);
        Assert.Equal("S", line.Direction);
        Assert.Equal(300m, line.ValueOut);
    }

    [Fact]
    public async Task Build_IcmsComplementIsIgnored()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.PurchaseInvoices.Add(Buy("C1", Oct05, 10m, 100m));
        var complement = Buy("CI", Oct10, 0m, 0m);
        complement.InvoiceType = PurchaseInvoiceType.IcmsComplement;
        complement.Items.First().IcmsValue = 50m;
        var saleComplement = Sell("VI", Oct10, 0m);
        saleComplement.InvoiceType = SalesInvoiceType.IcmsComplement;
        db.Context.PurchaseInvoices.Add(complement);
        db.Context.SalesInvoices.Add(saleComplement);
        await Save(db);

        var item = Assert.Single((await Service(db).BuildAsync(October())).Items);

        Assert.Equal(new[] { "C1" }, item.Lines.Select(l => l.InternalNumber));
    }

    [Fact]
    public async Task Build_ValueComplementAddsToOriginLayer()
    {
        var db = TestDb.CreateUnitOfWork();
        var purchase = Buy("C1", Oct05, 10m, 100m);
        db.Context.PurchaseInvoices.Add(purchase);
        var complement = Buy("CV", Oct10, 0m, 0m);
        complement.InvoiceType = PurchaseInvoiceType.ValueComplement;
        complement.Items.First().ComplementAmount = 50m;
        complement.Items.First().ComplementedItemKey = purchase.Items.First().Key;
        db.Context.PurchaseInvoices.Add(complement);
        await Save(db);

        var item = Assert.Single((await Service(db).BuildAsync(October())).Items);

        var line = item.Lines.Single(l => l.InternalNumber == "CV");
        Assert.Equal("Complemento de valor", line.Kind);
        Assert.Equal(0m, line.QuantityIn);
        Assert.Equal(50m, line.ValueIn);
        Assert.Equal(1050m, item.ClosingValue);
    }

    [Fact]
    public async Task Build_DifferentUnitIsFlagged()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.PurchaseInvoices.Add(Buy("C1", Oct05, 10m, 100m));
        var other = Buy("C2", Oct10, 60m, 1m);
        other.Items.First().UnitOfMeasureCode = "SC";
        db.Context.PurchaseInvoices.Add(other);
        await Save(db);

        var item = Assert.Single((await Service(db).BuildAsync(October())).Items);

        Assert.Equal("TN", item.UnitOfMeasureCode);
        Assert.Null(item.Lines[0].Warning);
        Assert.Equal("Unidade diferente", item.Lines[1].Warning);
    }

    [Fact]
    public async Task Build_InsufficientBalanceIsFlagged()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.SalesInvoices.Add(Sell("V1", Oct05, 3m));
        db.Context.PurchaseInvoices.Add(Buy("C1", Oct10, 10m, 100m));
        await Save(db);

        var item = Assert.Single((await Service(db).BuildAsync(October())).Items);

        Assert.Equal(new[] { "Saída", "Entrada", "Acerto de saldo negativo" }, item.Lines.Select(l => l.Kind));
        Assert.Equal("Saldo insuficiente", item.Lines[0].Warning);
        Assert.Equal(-3m, item.Lines[0].BalanceQuantity);
        Assert.Equal(7m, item.ClosingQuantity);
        Assert.Equal(700m, item.ClosingValue);
    }

    [Fact]
    public async Task Build_PreviousBalanceComputedBeforeFrom()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.PurchaseInvoices.Add(Buy("SET", new DateTime(2026, 9, 10), 10m, 100m));
        db.Context.SalesInvoices.Add(Sell("VSET", new DateTime(2026, 9, 20), 4m));
        db.Context.SalesInvoices.Add(Sell("VOUT", Oct10, 1m));
        db.Context.SalesInvoices.Add(Sell("VNOV", new DateTime(2026, 11, 1), 1m));
        await Save(db);

        var item = Assert.Single((await Service(db).BuildAsync(October())).Items);

        Assert.Equal(6m, item.OpeningQuantity);
        Assert.Equal(600m, item.OpeningValue);
        Assert.Equal(new[] { "VOUT" }, item.Lines.Select(l => l.InternalNumber));
        Assert.Equal(5m, item.ClosingQuantity);
        Assert.Equal(500m, item.ClosingValue);
    }

    [Fact]
    public async Task Build_BranchAndItemFilter()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.PurchaseInvoices.Add(Buy("C1", Oct05, 10m, 100m));
        db.Context.PurchaseInvoices.Add(Buy("C2", Oct05, 10m, 100m, branch: "02"));
        var other = Buy("C3", Oct05, 10m, 100m);
        other.Items.First().ItemCode = "20002";
        db.Context.PurchaseInvoices.Add(other);
        await Save(db);

        var all = await Service(db).BuildAsync(October());
        var one = await Service(db).BuildAsync(October(itemCode: "20002"));

        Assert.Equal(new[] { "10001", "20002" }, all.Items.Select(i => i.ItemCode));
        Assert.Equal(new[] { "C3" }, Assert.Single(one.Items).Lines.Select(l => l.InternalNumber));
    }

    [Fact]
    public async Task OpenLayers_ReturnsRemainingLayersAtDate()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.FiscalInventoryOpeningBalances.Add(Opening(10m, 1000m, Sep30));
        db.Context.PurchaseInvoices.Add(Buy("C1", Oct05, 10m, 120m));
        db.Context.SalesInvoices.Add(Sell("V1", Oct10, 15m));
        await Save(db);

        var before = await Service(db).OpenLayersAsync("01", "10001", Oct05);
        var after = await Service(db).OpenLayersAsync("01", null, Oct10);

        Assert.Equal(2200m, before.Sum(l => l.Value));
        var layer = Assert.Single(after);
        Assert.Equal(5m, layer.Quantity);
        Assert.Equal(600m, layer.Value);
    }

    private static FiscalInventoryOpeningBalance Opening(decimal qty, decimal value, DateTime date, string itemCode = "10001") => new()
    {
        Key = Guid.NewGuid(),
        BranchCode = "01",
        ItemCode = itemCode,
        ItemName = "SOJA EM GRÃOS",
        BalanceDate = date,
        Quantity = qty,
        TotalValue = value,
        UnitOfMeasureCode = "TN",
    };

    private static PurchaseInvoice Buy(string number, DateTime date, decimal qty, decimal unitPrice, string branch = "01", string cfop = "1101")
    {
        var item = PurchaseItem(quantity: qty, unitPrice: unitPrice);
        item.Cfop = cfop;
        item.MovesFiscalInventory = true;
        return Purchase(number, date, branchCode: branch, items: [item]);
    }

    private static SalesInvoice Sell(string number, DateTime date, decimal qty, string cfop = "5101")
    {
        var item = SaleItem(quantity: qty, unitPrice: 200m);
        item.Cfop = cfop;
        item.MovesFiscalInventory = true;
        var sale = Sale(number, date, items: item);
        sale.NfeStatus = NfeStatus.Authorized;
        return sale;
    }

    private static FiscalKardexRequest October(string? itemCode = null) => new()
    {
        BranchCode = "01",
        ItemCode = itemCode,
        FromDate = new DateTime(2026, 10, 1),
        ToDate = new DateTime(2026, 10, 31),
    };

    private static FiscalKardexService Service(IUnitOfWork db) => new(db);

    private static async Task Save(IUnitOfWork db)
    {
        await db.Context.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();
    }
}
