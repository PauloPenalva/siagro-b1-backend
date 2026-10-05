using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Models;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.SalesInvoices;

/// <summary>
/// Devolução própria (spec §7): o mesmo motor da venda com a natureza de ENTRADA, IBS/CBS pela data
/// da venda e a conferência contra a linha vendida.
/// </summary>
public class SalesInvoicesNfeReturnTaxApplyTests
{
    private const string CardBa = "C-BA";
    private const string CardSp = "C-SP";

    private static FakeBusinessPartnerService Partners() => new(
        names: new() { [CardBa] = "CLIENTE BA", [CardSp] = "CLIENTE SP" },
        states: new() { [CardBa] = "BA", [CardSp] = "SP" });

    private sealed record Scenario(UnitOfWork Db, int SaleUsage, int ReturnUsage, SalesInvoice Sale);

    /// <summary>Venda de grãos com ICMS 51 + cBenef e IBS/CBS 200, já calculada e confirmada.</summary>
    private static async Task<Scenario> SeedAsync(
        string card = CardSp, Action<UsageModel>? tweakReturn = null, bool withLaterRate = false)
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.Branchs.Add(new Branch { Code = "01", BranchName = "CEAGUI", StateCode = "SP", TaxRegime = TaxRegime.Normal, IssuesNfe = true });
        db.Context.Items.Add(new Item { ItemCode = "SOJA", ItemName = "SOJA", GoodsOrigin = 0, Ncm = "12019000" });
        db.Context.IbsCbsRates.Add(new IbsCbsRate { StartDate = new DateOnly(2026, 1, 1), CbsRate = 0.9m, IbsStateRate = 0.1m, IbsMunicipalRate = 0m });
        if (withLaterRate)
            db.Context.IbsCbsRates.Add(new IbsCbsRate { StartDate = new DateOnly(2026, 10, 5), CbsRate = 1.0m, IbsStateRate = 0.2m, IbsMunicipalRate = 0m });
        await db.SaveChangesAsync();

        var usages = new UsageService(db, NullLogger<UsageService>.Instance);
        var returnModel = new UsageModel
        {
            Name = "Entrada devolução", Direction = UsageDirection.Incoming,
            CfopIncomingInState = "1202", CfopIncomingOutState = "2202",
            IcmsInStateCst = "51", IcmsInStateRate = 18m, IcmsInStateDeferral = 100m, IcmsInStateBenefitCode = "SP053521",
            IcmsOutStateCst = "00", PisCst = "72", CofinsCst = "72",
            IbsCbsCst = "200", IbsCbsClassCode = "200036", IbsRateReduction = 60m, CbsRateReduction = 60m,
            RequiresQuantity = true,
        };
        tweakReturn?.Invoke(returnModel);
        var ret = await usages.CreateAsync(returnModel);

        var sale = await usages.CreateAsync(new UsageModel
        {
            Name = "Venda suspensão", Direction = UsageDirection.Outgoing,
            CfopOutgoingInState = "5102", CfopOutgoingOutState = "6102",
            IcmsInStateCst = "51", IcmsInStateRate = 18m, IcmsInStateDeferral = 100m, IcmsInStateBenefitCode = "SP053521",
            IcmsOutStateCst = "00", PisCst = "09", CofinsCst = "09",
            IbsCbsCst = "200", IbsCbsClassCode = "200036", IbsRateReduction = 60m, CbsRateReduction = 60m,
            RequiresQuantity = true, ReturnUsageCode = ret.Code,
        });

        var origin = new SalesInvoice
        {
            Key = Guid.NewGuid(), BranchCode = "01", CardCode = card, InvoiceDate = new DateTime(2026, 10, 1),
            Items = [new SalesInvoiceItem { Key = Guid.NewGuid(), ItemCode = "SOJA", UnitOfMeasureCode = "KG", Quantity = 30000m, UnitPrice = 2m, UsageCode = sale.Code }],
        };
        await TaxTestServices.Apply(db, Partners()).ApplyAsync(origin, origin.Items);
        origin.InvoiceStatus = InvoiceStatus.Confirmed;
        db.Context.SalesInvoices.Add(origin);
        await db.SaveChangesAsync();

        return new Scenario(db, sale.Code, ret.Code, origin);
    }

    private static SalesInvoice OwnReturn(Scenario s, decimal quantity = 30000m, int? usageCode = -1) => new()
    {
        Key = Guid.NewGuid(), BranchCode = "01", CardCode = s.Sale.CardCode, InvoiceDate = new DateTime(2026, 10, 10),
        InvoiceType = SalesInvoiceType.Return, IsNfeReturn = true, SalesInvoiceOriginKey = s.Sale.Key,
        Items =
        [
            new SalesInvoiceItem
            {
                Key = Guid.NewGuid(), ItemCode = "SOJA", UnitOfMeasureCode = "KG", Quantity = quantity, UnitPrice = 2m,
                UsageCode = usageCode == -1 ? s.ReturnUsage : usageCode,
                SalesInvoiceItemOriginKey = s.Sale.Items.Single().Key,
            },
        ],
    };

    private static async Task<SalesInvoiceItem> Apply(Scenario s, SalesInvoice invoice)
    {
        await TaxTestServices.Apply(s.Db, Partners()).ApplyAsync(invoice, invoice.Items);
        return invoice.Items.Single();
    }

    private static async Task<string> Rejects(Scenario s, SalesInvoice invoice)
    {
        var ex = await Assert.ThrowsAsync<DefaultException>(() => Apply(s, invoice));
        return ex.Message;
    }

    [Fact]
    public async Task Own_return_in_state_uses_the_incoming_cfop_and_the_return_usage()
    {
        var s = await SeedAsync();

        var item = await Apply(s, OwnReturn(s));

        Assert.Equal("1202", item.Cfop);
        Assert.Equal("51", item.CstIcms);
        Assert.Equal("72", item.CstPis);
        Assert.Equal("Entrada devolução", item.UsageName);
    }

    [Fact]
    public async Task Own_return_out_of_state_uses_the_interstate_incoming_cfop()
    {
        var s = await SeedAsync(CardBa);

        Assert.Equal("2202", (await Apply(s, OwnReturn(s))).Cfop);
    }

    [Fact]
    public async Task Ibs_cbs_rates_come_from_the_sale_date()
    {
        var s = await SeedAsync(withLaterRate: true);

        var item = await Apply(s, OwnReturn(s));

        Assert.Equal(0.9m, item.CbsRate);
        Assert.Equal(0.1m, item.IbsStateRate);
    }

    [Fact]
    public async Task Partial_return_values_follow_the_returned_quantity()
    {
        var s = await SeedAsync();

        var item = await Apply(s, OwnReturn(s, quantity: 10000m));

        Assert.Equal(20000m, item.IcmsBase);
        Assert.Equal(3600m, item.IcmsOperationValue);
    }

    [Fact]
    public async Task Outgoing_usage_on_an_own_return_is_rejected()
    {
        var s = await SeedAsync();

        Assert.Contains("é de saída e não pode ser usada na devolução", await Rejects(s, OwnReturn(s, usageCode: s.SaleUsage)));
    }

    [Fact]
    public async Task Own_return_line_without_usage_is_rejected()
    {
        var s = await SeedAsync();

        Assert.Contains("está sem natureza de devolução", await Rejects(s, OwnReturn(s, usageCode: null)));
    }

    [Fact]
    public async Task Different_icms_rate_is_refused_by_the_conference()
    {
        var s = await SeedAsync(tweakReturn: u => u.IcmsInStateRate = 12m);

        Assert.Equal(
            "Item SOJA: a natureza de devolução Entrada devolução não reproduz a tributação da venda — alíquota do ICMS: venda 18, devolução 12.",
            await Rejects(s, OwnReturn(s)));
    }

    [Fact]
    public async Task Missing_benefit_code_is_refused_by_the_conference()
    {
        var s = await SeedAsync(tweakReturn: u => u.IcmsInStateBenefitCode = null);

        Assert.Contains("cBenef: venda SP053521, devolução (vazio)", await Rejects(s, OwnReturn(s)));
    }

    [Fact]
    public async Task Different_ibs_cbs_class_is_refused_by_the_conference()
    {
        var s = await SeedAsync(tweakReturn: u => u.IbsCbsClassCode = "200034");

        Assert.Contains("classificação do IBS/CBS: venda 200036, devolução 200034", await Rejects(s, OwnReturn(s)));
    }

    [Fact]
    public async Task Return_without_the_flag_is_still_skipped()
    {
        var s = await SeedAsync();
        var plain = OwnReturn(s);
        plain.IsNfeReturn = false;
        plain.Items.Single().IcmsValue = 123m;

        Assert.Equal(123m, (await Apply(s, plain)).IcmsValue);
    }
}
