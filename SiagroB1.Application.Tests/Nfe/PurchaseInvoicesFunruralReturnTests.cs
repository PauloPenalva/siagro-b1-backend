using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Nfe;
using SiagroB1.Application.Services.PurchaseInvoices;
using SiagroB1.Application.Services.Taxes;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;

namespace SiagroB1.Application.Tests.Nfe;

/// <summary>A devolução de compra estorna o Funrural com as alíquotas da COMPRA (spec 2026-10-09 §5).</summary>
public class PurchaseInvoicesFunruralReturnTests
{
    private static FakeBusinessPartnerService Partners() => new(
        names: new Dictionary<string, string> { [PurchaseNfeTestSeed.Supplier] = "PRODUTOR RURAL TESTE" },
        states: new Dictionary<string, string> { [PurchaseNfeTestSeed.Supplier] = "SP" });

    private static PurchaseInvoicesNfeReturnCreateService Returns(PurchaseNfeScenario scenario) =>
        new(scenario.Db, new TaxCalculationGate(scenario.Db, NfeTestSeed.Config()),
            new PurchaseInvoicesCreateService(scenario.Db, Partners(), new FakeItemService(),
                TaxTestServices.PurchaseApply(scenario.Db, Partners()), new FakeDocNumberSequenceService()),
            NfeTestSeed.Clock, NfeIssueInputAssembler.BrasiliaZone);

    /// <summary>Entrada do cenário (1.000 kg × 1,50) com o Funrural informado, autorizada e confirmada.</summary>
    private static async Task<(PurchaseNfeScenario Scenario, PurchaseInvoice Origin)> AuthorizedOriginAsync(
        decimal inss, decimal rat, decimal senar)
    {
        var scenario = await PurchaseNfeTestSeed.SeedAsync();
        var invoice = await scenario.Db.Context.PurchaseInvoices.Include(i => i.Items).SingleAsync(i => i.Key == scenario.InvoiceKey);
        PurchaseInvoiceFunrural.Apply(invoice.Items.Single(), inss, rat, senar);
        await scenario.Db.Context.SaveChangesAsync();

        var sefaz = new FakeNfeSefazClient();
        sefaz.AuthorizeResponses.Enqueue(key => FakeNfeSefazClient.Authorized(key));
        await PurchaseInvoicesNfeIssueServiceTests.Issue(scenario, sefaz).ExecuteAsync(scenario.InvoiceKey, "tester");
        scenario.Db.Context.ChangeTracker.Clear();

        return (scenario, await PurchaseInvoicesNfeIssueServiceTests.ReloadAsync(scenario));
    }

    /// <summary>Muda as alíquotas das duas naturezas DEPOIS da compra: a devolução não pode lê-las.</summary>
    private static async Task ChangeNaturesAsync(PurchaseNfeScenario scenario)
    {
        foreach (var usage in await scenario.Db.Context.Usages.ToListAsync())
        {
            usage.FunruralInssRate = 1.98m;
            usage.FunruralRatRate = 0m;
            usage.FunruralSenarRate = 0.25m;
        }
        await scenario.Db.Context.SaveChangesAsync();
        scenario.Db.Context.ChangeTracker.Clear();
    }

    private static PurchaseInvoiceNfeReturnRequest Request(PurchaseInvoice origin, decimal quantity) =>
        new(origin.Key, [new PurchaseInvoiceNfeReturnItem(origin.Items.Single().Key!.Value, quantity)], "grão fora do padrão");

    [Fact]
    public async Task Return_copies_the_rates_of_the_bought_line()
    {
        var (scenario, origin) = await AuthorizedOriginAsync(1.32m, 0.11m, 0.20m);
        await ChangeNaturesAsync(scenario);

        var created = await Returns(scenario).ExecuteAsync(Request(origin, 400m), "tester");

        var line = (await PurchaseInvoicesNfeIssueServiceTests.ReloadAsync(scenario, created.Key)).Items.Single();
        Assert.Equal((600m, 1.32m, 0.11m, 0.20m),
            (line.FunruralBase, line.FunruralInssRate, line.FunruralRatRate, line.FunruralSenarRate));
        Assert.Equal((7.92m, 0.66m, 1.20m), (line.FunruralInssValue, line.FunruralRatValue, line.FunruralSenarValue));
    }

    [Fact]
    public async Task Senar_only_purchase_returns_with_senar_only()
    {
        var (scenario, origin) = await AuthorizedOriginAsync(0m, 0m, 0.20m);
        await ChangeNaturesAsync(scenario);

        var created = await Returns(scenario).ExecuteAsync(Request(origin, 1000m), "tester");

        var line = (await PurchaseInvoicesNfeIssueServiceTests.ReloadAsync(scenario, created.Key)).Items.Single();
        Assert.Equal((0m, 0m, 3.00m), (line.FunruralInssValue, line.FunruralRatValue, line.FunruralSenarValue));
    }

    [Fact]
    public async Task Purchase_without_funrural_returns_without_funrural()
    {
        var (scenario, origin) = await AuthorizedOriginAsync(0m, 0m, 0m);
        await ChangeNaturesAsync(scenario);

        var created = await Returns(scenario).ExecuteAsync(Request(origin, 400m), "tester");

        var line = (await PurchaseInvoicesNfeIssueServiceTests.ReloadAsync(scenario, created.Key)).Items.Single();
        Assert.Equal((0m, 0m), (line.FunruralBase, line.FunruralValue));
    }
}
