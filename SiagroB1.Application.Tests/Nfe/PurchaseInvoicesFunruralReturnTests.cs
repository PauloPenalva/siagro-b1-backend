using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Nfe;
using SiagroB1.Application.Services.PurchaseInvoices;
using SiagroB1.Application.Services.Taxes;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Application.Tests.Nfe;

/// <summary>A devolução de compra estorna o Funrural com as alíquotas da COMPRA (spec 2026-10-09 §5).</summary>
public class PurchaseInvoicesFunruralReturnTests
{
    private static PurchaseInvoicesNfeReturnCreateService Returns(
        PurchaseNfeScenario scenario, FakeBusinessPartnerService? partners = null)
    {
        partners ??= PurchaseNfeTestSeed.Partners();
        return new PurchaseInvoicesNfeReturnCreateService(scenario.Db, new TaxCalculationGate(scenario.Db, NfeTestSeed.Config()),
            new PurchaseInvoicesCreateService(scenario.Db, partners, new FakeItemService(),
                TaxTestServices.PurchaseApply(scenario.Db, partners), new FakeDocNumberSequenceService()),
            NfeTestSeed.Clock, NfeIssueInputAssembler.BrasiliaZone);
    }

    /// <summary>
    /// Entrada do cenário (1.000 kg × 1,50) calculada pela regra — natureza no <paramref name="mode"/>, F-SP produtor
    /// pessoa física, tabela semeada —, autorizada e confirmada.
    /// </summary>
    private static async Task<(PurchaseNfeScenario Scenario, PurchaseInvoice Origin)> AuthorizedOriginAsync(
        RuralContributionMode mode)
    {
        var scenario = await PurchaseNfeTestSeed.SeedAsync();
        var code = await PurchaseNfeTestSeed.PurchaseUsageCodeAsync(scenario.Db);
        (await scenario.Db.Context.Usages.SingleAsync(u => u.Code == code)).RuralContributionMode = mode;
        await scenario.Db.Context.SaveChangesAsync();
        var invoice = await scenario.Db.Context.PurchaseInvoices.Include(i => i.Items).SingleAsync(i => i.Key == scenario.InvoiceKey);
        await TaxTestServices.PurchaseApply(scenario.Db, PurchaseNfeTestSeed.Partners()).ApplyAsync(invoice, invoice.Items);
        await scenario.Db.Context.SaveChangesAsync();

        var sefaz = new FakeNfeSefazClient();
        sefaz.AuthorizeResponses.Enqueue(key => FakeNfeSefazClient.Authorized(key));
        await PurchaseInvoicesNfeIssueServiceTests.Issue(scenario, sefaz).ExecuteAsync(scenario.InvoiceKey, "tester");
        scenario.Db.Context.ChangeTracker.Clear();

        return (scenario, await PurchaseInvoicesNfeIssueServiceTests.ReloadAsync(scenario));
    }

    /// <summary>Tira a contribuição social de todas as naturezas DEPOIS da compra: a devolução não pode lê-las.</summary>
    private static async Task ChangeNaturesAsync(PurchaseNfeScenario scenario)
    {
        foreach (var usage in await scenario.Db.Context.Usages.ToListAsync())
            usage.RuralContributionMode = RuralContributionMode.None;
        await scenario.Db.Context.SaveChangesAsync();
        scenario.Db.Context.ChangeTracker.Clear();
    }

    private static PurchaseInvoiceNfeReturnRequest Request(PurchaseInvoice origin, decimal quantity) =>
        new(origin.Key, [new PurchaseInvoiceNfeReturnItem(origin.Items.Single().Key!.Value, quantity)], "grão fora do padrão");

    [Fact]
    public async Task Return_copies_the_rates_of_the_bought_line()
    {
        var (scenario, origin) = await AuthorizedOriginAsync(RuralContributionMode.Full);
        await ChangeNaturesAsync(scenario);

        var created = await Returns(scenario).ExecuteAsync(Request(origin, 400m), "tester");

        var line = (await PurchaseInvoicesNfeIssueServiceTests.ReloadAsync(scenario, created.Key)).Items.Single();
        Assert.Equal((600m, 1.32m, 0.11m, 0.20m),
            (line.FunruralBase, line.FunruralInssRate, line.FunruralRatRate, line.FunruralSenarRate));
        Assert.Equal((7.92m, 0.66m, 1.20m), (line.FunruralInssValue, line.FunruralRatValue, line.FunruralSenarValue));
    }

    [Fact]
    public async Task Return_copies_origin_rates_even_after_partner_changes()
    {
        var (scenario, origin) = await AuthorizedOriginAsync(RuralContributionMode.Full);

        // Depois da compra o F-SP vira produtor PJ: a entrada nova não reteria, mas a devolução estorna o que foi retido.
        var created = await Returns(scenario, PurchaseNfeTestSeed.PartnersAs(RuralProducerType.LegalEntity))
            .ExecuteAsync(Request(origin, 400m), "tester");

        var line = (await PurchaseInvoicesNfeIssueServiceTests.ReloadAsync(scenario, created.Key)).Items.Single();
        Assert.Equal((600m, 1.32m, 0.11m, 0.20m),
            (line.FunruralBase, line.FunruralInssRate, line.FunruralRatRate, line.FunruralSenarRate));
        Assert.Equal((7.92m, 0.66m, 1.20m), (line.FunruralInssValue, line.FunruralRatValue, line.FunruralSenarValue));
    }

    [Fact]
    public async Task Senar_only_purchase_returns_with_senar_only()
    {
        var (scenario, origin) = await AuthorizedOriginAsync(RuralContributionMode.SenarOnly);
        await ChangeNaturesAsync(scenario);

        var created = await Returns(scenario).ExecuteAsync(Request(origin, 1000m), "tester");

        var line = (await PurchaseInvoicesNfeIssueServiceTests.ReloadAsync(scenario, created.Key)).Items.Single();
        Assert.Equal((0m, 0m, 3.00m), (line.FunruralInssValue, line.FunruralRatValue, line.FunruralSenarValue));
    }

    [Fact]
    public async Task Purchase_without_funrural_returns_without_funrural()
    {
        var (scenario, origin) = await AuthorizedOriginAsync(RuralContributionMode.None);
        await ChangeNaturesAsync(scenario);

        var created = await Returns(scenario).ExecuteAsync(Request(origin, 400m), "tester");

        var line = (await PurchaseInvoicesNfeIssueServiceTests.ReloadAsync(scenario, created.Key)).Items.Single();
        Assert.Equal((0m, 0m), (line.FunruralBase, line.FunruralValue));
    }

    [Fact]
    public async Task Return_nfe_carries_the_reversal_text()
    {
        var (scenario, origin) = await AuthorizedOriginAsync(RuralContributionMode.Full);
        var created = await Returns(scenario).ExecuteAsync(Request(origin, 400m), "tester");
        scenario.Db.Context.ChangeTracker.Clear();
        var sefaz = new FakeNfeSefazClient();
        sefaz.AuthorizeResponses.Enqueue(key => FakeNfeSefazClient.Authorized(key));

        await PurchaseInvoicesNfeIssueServiceTests.Issue(scenario, sefaz).ExecuteAsync(created.Key, "tester");

        var infCpl = XDocument.Parse(Assert.Single(sefaz.Sent).Xml).Descendants().Single(e => e.Name.LocalName == "infCpl").Value;
        Assert.Contains(
            "Estorno - Contribuição Social denominada Funrural 1,63% = R$ 9,78 (INSS 1,32% R$ 7,92; RAT 0,11% R$ 0,66; SENAR 0,20% R$ 1,20)",
            infCpl);
    }
}
