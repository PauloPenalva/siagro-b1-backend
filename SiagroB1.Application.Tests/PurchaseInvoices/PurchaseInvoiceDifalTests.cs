using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services;
using SiagroB1.Application.Services.PurchaseInvoices;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Models;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.PurchaseInvoices;

/// <summary>Spec 2026-10-17 §2: DIFAL e FCP gravados na linha do Documento de Entrada de terceiro.</summary>
public class PurchaseInvoiceDifalTests
{
    private const string Supplier = "F-PR";
    private const string ItemCode = "BOMBA";

    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private sealed record Scenario(PurchaseInvoice Invoice, int Consumption, int Resale);

    private static FakeBusinessPartnerService Partners(string state = "PR") => new(
        names: new Dictionary<string, string> { [Supplier] = "FORNECEDOR" },
        states: new Dictionary<string, string> { [Supplier] = state });

    private static FakeItemService Catalog() => new(new Dictionary<string, string> { [ItemCode] = "BOMBA D'AGUA" });

    private async Task<Scenario> SeedAsync(
        bool stateSetting = true, DocumentIssuerType issuer = DocumentIssuerType.ThirdParty,
        PurchaseInvoiceType type = PurchaseInvoiceType.Normal)
    {
        _db.Context.Branchs.Add(new Branch
        {
            Code = "01", BranchName = "FILIAL", ShortName = "FILIAL", TaxId = "12345678000195", StateCode = "SP",
            TaxRegime = TaxRegime.Normal, IssuesNfe = false,
        });
        _db.Context.Items.Add(new Item { ItemCode = ItemCode, ItemName = "BOMBA D'AGUA", GoodsOrigin = 0 });
        if (stateSetting)
            _db.Context.IcmsStateSettings.Add(new IcmsStateSetting
                { StateCode = "SP", InternalRate = 18m, FcpRate = 0m, DifalBaseRule = DifalBaseRule.Double });

        var usages = new UsageService(_db, NullLogger<UsageService>.Instance);
        var consumption = (await usages.CreateAsync(Usage("Uso e consumo", PurchasePurpose.UseAndConsumption))).Code;
        var resale = (await usages.CreateAsync(Usage("Revenda", PurchasePurpose.ResaleOrInput))).Code;

        var invoice = new PurchaseInvoice
        {
            Key = Guid.NewGuid(), BranchCode = "01", CardCode = Supplier, InvoiceType = type, IssuerType = issuer,
            InvoiceStatus = InvoiceStatus.Pending, TaxDocumentNumber = "9001", TaxDocumentSeries = "1",
            IssueDate = new DateTime(2026, 10, 12),
        };
        _db.Context.PurchaseInvoices.Add(invoice);
        await _db.Context.SaveChangesAsync();
        _db.Context.ChangeTracker.Clear();
        return new Scenario(invoice, consumption, resale);
    }

    private static UsageModel Usage(string name, PurchasePurpose purpose) => new()
    {
        Name = name, Direction = UsageDirection.Incoming, CfopIncomingInState = "1556", CfopIncomingOutState = "2556",
        PurchasePurpose = purpose,
    };

    private static PurchaseInvoiceItem Line(Guid invoice, int usage, decimal price = 1000m, decimal icmsRate = 0m) => new()
    {
        Key = Guid.NewGuid(), PurchaseInvoiceKey = invoice, ItemCode = ItemCode, ItemName = "", UnitOfMeasureCode = "UN",
        Quantity = 1m, UnitPrice = price, UsageCode = usage, IcmsRate = icmsRate,
    };

    private PurchaseInvoicesTaxApplyService Apply(string erp = "STANDALONE", string state = "PR") =>
        TaxTestServices.PurchaseApply(_db, Partners(state), erp);

    private Task AddAsync(PurchaseInvoiceItem line, string erp = "STANDALONE", string state = "PR") =>
        new PurchaseInvoicesItemsCreateService(_db, Catalog(), Apply(erp, state)).ExecuteAsync(line, "tester");

    private async Task<PurchaseInvoiceItem> ReloadAsync(Guid? key)
    {
        _db.Context.ChangeTracker.Clear();
        return await _db.Context.PurchaseInvoicesItems.AsNoTracking().FirstAsync(x => x.Key == key);
    }

    [Fact]
    public async Task Interstate_use_and_consumption_computes_double_base_difal()
    {
        var s = await SeedAsync();
        var line = Line(s.Invoice.Key, s.Consumption);

        await AddAsync(line);

        var saved = await ReloadAsync(line.Key);
        Assert.Equal(1073.17m, saved.DifalBase);
        Assert.Equal((18m, 12m, 73.17m), (saved.DifalInternalRate, saved.DifalInterstateRate, saved.DifalValue));
        Assert.Equal((0m, 0m), (saved.FcpRate, saved.FcpValue));
    }

    [Fact]
    public async Task Resale_purpose_has_no_difal()
    {
        var s = await SeedAsync();
        var line = Line(s.Invoice.Key, s.Resale);

        await AddAsync(line);

        Assert.Equal(0m, (await ReloadAsync(line.Key)).DifalValue);
    }

    [Fact]
    public async Task Same_state_has_no_difal()
    {
        var s = await SeedAsync();
        var line = Line(s.Invoice.Key, s.Consumption);

        await AddAsync(line, state: "SP");

        var saved = await ReloadAsync(line.Key);
        Assert.Equal((0m, 0m, 0m), (saved.DifalBase, saved.DifalValue, saved.DifalInternalRate));
    }

    [Fact]
    public async Task Foreign_supplier_has_no_difal()
    {
        var s = await SeedAsync();
        var line = Line(s.Invoice.Key, s.Consumption);

        await AddAsync(line, state: "EX");

        Assert.Equal(0m, (await ReloadAsync(line.Key)).DifalValue);
    }

    [Fact]
    public async Task Changing_line_usage_to_resale_clears_difal()
    {
        var s = await SeedAsync();
        var line = Line(s.Invoice.Key, s.Consumption);
        await AddAsync(line);
        Assert.Equal(73.17m, (await ReloadAsync(line.Key)).DifalValue);

        var body = Line(s.Invoice.Key, s.Resale);
        await new PurchaseInvoicesItemsUpdateService(_db, Catalog(), Apply()).ExecuteAsync(line.Key!.Value, body, "tester");

        var saved = await ReloadAsync(line.Key);
        Assert.Equal((0m, 0m, 0m, 0m), (saved.DifalBase, saved.DifalValue, saved.DifalInternalRate, saved.DifalInterstateRate));
    }

    [Fact]
    public async Task Missing_state_rate_saves_with_zero_and_confirm_refuses()
    {
        var s = await SeedAsync(stateSetting: false);
        var line = Line(s.Invoice.Key, s.Consumption);

        await AddAsync(line);

        Assert.Equal(0m, (await ReloadAsync(line.Key)).DifalValue);

        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            TaxTestServices.PurchaseConfirm(_db, "STANDALONE", Partners()).ExecuteAsync(s.Invoice.Key, "tester"));

        Assert.Equal("Cadastre a alíquota interna de ICMS da UF SP em ICMS por UF.", ex.Message);
        _db.Context.ChangeTracker.Clear();
        Assert.Equal(InvoiceStatus.Pending,
            (await _db.Context.PurchaseInvoices.AsNoTracking().FirstAsync(x => x.Key == s.Invoice.Key)).InvoiceStatus);
    }

    [Fact]
    public async Task Item_rate_overrides_state_rate_on_line()
    {
        var s = await SeedAsync();
        _db.Context.IcmsItemStateRates.Add(new IcmsItemStateRate { Key = Guid.NewGuid(), ItemCode = ItemCode, StateCode = "SP", InternalRate = 20m });
        await _db.Context.SaveChangesAsync();
        var line = Line(s.Invoice.Key, s.Consumption);

        await AddAsync(line);

        var saved = await ReloadAsync(line.Key);
        Assert.Equal((1100m, 20m, 100m), (saved.DifalBase, saved.DifalInternalRate, saved.DifalValue));
    }

    [Fact]
    public async Task Difal_does_not_change_net_payable_or_grand_total()
    {
        var s = await SeedAsync();
        await AddAsync(Line(s.Invoice.Key, s.Consumption));

        _db.Context.ChangeTracker.Clear();
        var invoice = await _db.Context.PurchaseInvoices.Include(i => i.Items).FirstAsync(x => x.Key == s.Invoice.Key);

        Assert.Equal(73.17m, invoice.Items.Single().DifalValue);
        Assert.Equal(1000m, invoice.GrandTotal);
        Assert.Equal(1000m, invoice.NetPayable);
    }

    [Fact]
    public async Task Sapb1_mode_never_computes_difal()
    {
        var s = await SeedAsync();
        var line = Line(s.Invoice.Key, s.Consumption);

        await AddAsync(line, erp: "SAPB1");

        Assert.Equal((0m, 0m), ((await ReloadAsync(line.Key)).DifalBase, (await ReloadAsync(line.Key)).DifalValue));
    }
}
