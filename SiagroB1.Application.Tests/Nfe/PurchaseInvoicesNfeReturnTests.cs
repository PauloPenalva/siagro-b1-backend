using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Nfe;
using SiagroB1.Application.Services.PurchaseInvoices;
using SiagroB1.Application.Services.Taxes;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Application.Tests.Nfe;

/// <summary>"Devolver" a entrada própria autorizada e emitir a devolução de compra (spec §9).</summary>
public class PurchaseInvoicesNfeReturnTests
{
    private static FakeBusinessPartnerService Partners() => new(
        names: new Dictionary<string, string> { [PurchaseNfeTestSeed.Supplier] = "PRODUTOR RURAL TESTE" },
        states: new Dictionary<string, string> { [PurchaseNfeTestSeed.Supplier] = "SP" });

    private static PurchaseInvoicesCreateService CreateService(PurchaseNfeScenario scenario) =>
        new(scenario.Db, Partners(), new FakeItemService(), TaxTestServices.PurchaseApply(scenario.Db, Partners()));

    private static PurchaseInvoicesNfeReturnCreateService Returns(PurchaseNfeScenario scenario, string erp = "STANDALONE") =>
        new(scenario.Db, new TaxCalculationGate(scenario.Db, NfeTestSeed.Config(erp)), CreateService(scenario),
            NfeTestSeed.Clock, NfeIssueInputAssembler.BrasiliaZone);

    /// <summary>A entrada do cenário autorizada e confirmada pela emissão (é o que dá chave e nItem à origem).</summary>
    private static async Task<(PurchaseNfeScenario Scenario, PurchaseInvoice Origin)> AuthorizedOriginAsync(bool twoItems = false)
    {
        var scenario = await PurchaseNfeTestSeed.SeedAsync(twoItems);
        var sefaz = new FakeNfeSefazClient();
        sefaz.AuthorizeResponses.Enqueue(key => FakeNfeSefazClient.Authorized(key));
        await PurchaseInvoicesNfeIssueServiceTests.Issue(scenario, sefaz).ExecuteAsync(scenario.InvoiceKey, "tester");
        scenario.Db.Context.ChangeTracker.Clear();

        return (scenario, await PurchaseInvoicesNfeIssueServiceTests.ReloadAsync(scenario));
    }

    private static PurchaseInvoiceNfeReturnRequest Request(PurchaseInvoice origin, decimal quantity, string reason = "grão fora do padrão", int item = 0) =>
        new(origin.Key, [new PurchaseInvoiceNfeReturnItem(origin.Items.OrderBy(i => i.NfeItemNumber).ElementAt(item).Key!.Value, quantity)], reason);

    [Fact]
    public async Task Partial_return_is_born_pending_with_the_return_usage_and_proportional_weights()
    {
        var (scenario, origin) = await AuthorizedOriginAsync();

        var created = await Returns(scenario).ExecuteAsync(Request(origin, 400m), "tester");

        var saved = await PurchaseInvoicesNfeIssueServiceTests.ReloadAsync(scenario, created.Key);
        Assert.Equal(PurchaseInvoiceType.Return, saved.InvoiceType);
        Assert.Equal(DocumentIssuerType.Own, saved.IssuerType);
        Assert.True(saved.IsNfeReturn);
        Assert.Equal(InvoiceStatus.Pending, saved.InvoiceStatus);
        Assert.Equal(origin.Key, saved.PurchaseInvoiceOriginKey);
        Assert.Null(saved.PaymentConditionCode);
        Assert.Null(saved.ReferencedAccessKey);
        Assert.Equal(400m, saved.NetWeight);
        Assert.Equal(400m, saved.GrossWeight);
        var line = saved.Items.Single();
        Assert.Equal(400m, line.Quantity);
        Assert.Equal(1.5m, line.UnitPrice);
        Assert.Equal(scenario.ReturnUsage, line.UsageCode);
        Assert.Equal("5202", line.Cfop);
        Assert.StartsWith($"Devolução da NF-e 1 série 1. Motivo: grão fora do padrão", saved.Comments);
    }

    [Fact]
    public async Task Origin_is_left_untouched()
    {
        var (scenario, origin) = await AuthorizedOriginAsync();

        await Returns(scenario).ExecuteAsync(Request(origin, 1000m), "tester");

        var after = await PurchaseInvoicesNfeIssueServiceTests.ReloadAsync(scenario);
        Assert.Equal(InvoiceStatus.Confirmed, after.InvoiceStatus);
        Assert.Equal(1000m, after.Items.Single().Quantity);
    }

    [Fact]
    public async Task Quantity_above_the_returnable_balance_is_refused()
    {
        var (scenario, origin) = await AuthorizedOriginAsync();
        await Returns(scenario).ExecuteAsync(Request(origin, 600m), "tester");

        var e = await Assert.ThrowsAsync<DefaultException>(() => Returns(scenario).ExecuteAsync(Request(origin, 400.001m), "tester"));

        Assert.Equal("Item TRIGO: a quantidade a devolver (400,001) passa do saldo devolvível (400,000).", e.Message);
    }

    [Theory]
    [InlineData("", "Informe o motivo da devolução.")]
    [InlineData("   ", "Informe o motivo da devolução.")]
    public async Task Reason_is_required(string reason, string message)
    {
        var (scenario, origin) = await AuthorizedOriginAsync();

        var e = await Assert.ThrowsAsync<DefaultException>(() => Returns(scenario).ExecuteAsync(Request(origin, 10m, reason), "tester"));

        Assert.Equal(message, e.Message);
    }

    [Fact]
    public async Task Zero_quantity_is_refused()
    {
        var (scenario, origin) = await AuthorizedOriginAsync();

        var e = await Assert.ThrowsAsync<DefaultException>(() => Returns(scenario).ExecuteAsync(Request(origin, 0m), "tester"));

        Assert.Equal("Informe a quantidade a devolver de ao menos um item.", e.Message);
    }

    [Fact]
    public async Task Branch_that_does_not_issue_nfe_is_refused()
    {
        var (scenario, origin) = await AuthorizedOriginAsync();

        var e = await Assert.ThrowsAsync<DefaultException>(() => Returns(scenario, "SAPB1").ExecuteAsync(Request(origin, 10m), "tester"));

        Assert.Equal("A filial 01 não emite NF-e pelo Siagro.", e.Message);
    }

    [Fact]
    public async Task Entry_without_authorized_nfe_is_refused()
    {
        var scenario = await PurchaseNfeTestSeed.SeedAsync();
        var origin = await PurchaseInvoicesNfeIssueServiceTests.ReloadAsync(scenario);

        var e = await Assert.ThrowsAsync<DefaultException>(() => Returns(scenario).ExecuteAsync(Request(origin, 10m), "tester"));

        Assert.Equal(
            "O documento de entrada não tem NF-e própria autorizada pelo Siagro: a devolução de compra parte de uma entrada própria autorizada.",
            e.Message);
    }

    [Fact]
    public async Task Usage_without_return_usage_is_refused()
    {
        var (scenario, origin) = await AuthorizedOriginAsync();
        var usage = await scenario.Db.Context.Usages.SingleAsync(u => u.Name == "COMPRA DE MERCADORIA");
        usage.ReturnUsageCode = null;
        await scenario.Db.SaveChangesAsync();

        var e = await Assert.ThrowsAsync<DefaultException>(() => Returns(scenario).ExecuteAsync(Request(origin, 10m), "tester"));

        Assert.Equal($"A natureza {usage.Code} COMPRA DE MERCADORIA da compra não tem natureza de devolução cadastrada.", e.Message);
    }

    [Fact]
    public async Task Returning_only_the_second_item_references_its_entry_item_number()
    {
        // Review Focus 3.
        var (scenario, origin) = await AuthorizedOriginAsync(twoItems: true);
        var created = await Returns(scenario).ExecuteAsync(Request(origin, 100m, item: 1), "tester");
        var sefaz = new FakeNfeSefazClient();
        sefaz.AuthorizeResponses.Enqueue(key => FakeNfeSefazClient.Authorized(key));

        await PurchaseInvoicesNfeIssueServiceTests.Issue(scenario, sefaz).ExecuteAsync(created.Key, "tester");

        var xml = Assert.Single(sefaz.Sent).Xml;
        Assert.Contains("<tpNF>1</tpNF>", xml);
        Assert.Contains("<finNFe>4</finNFe>", xml);
        Assert.Contains($"<DFeReferenciado><chaveAcesso>{origin.ChaveNFe}</chaveAcesso><nItem>2</nItem></DFeReferenciado>", xml);
        Assert.DoesNotContain("<NFref>", xml);
        Assert.Contains("<tPag>90</tPag>", xml);
        Assert.Contains("Devolução da NF-e nº 1, série 1", xml);
    }

    [Fact]
    public async Task Authorized_return_is_confirmed()
    {
        var (scenario, origin) = await AuthorizedOriginAsync();
        var created = await Returns(scenario).ExecuteAsync(Request(origin, 400m), "tester");
        var sefaz = new FakeNfeSefazClient();
        sefaz.AuthorizeResponses.Enqueue(key => FakeNfeSefazClient.Authorized(key));

        var outcome = await PurchaseInvoicesNfeIssueServiceTests.Issue(scenario, sefaz).ExecuteAsync(created.Key, "tester");

        Assert.Equal(NfeStatus.Authorized, outcome.NfeStatus);
        Assert.Equal(InvoiceStatus.Confirmed, (await PurchaseInvoicesNfeIssueServiceTests.ReloadAsync(scenario, created.Key)).InvoiceStatus);
    }

    [Fact]
    public async Task Two_pending_returns_above_the_purchase_are_refused_before_the_number()
    {
        // Review Focus 4.
        var (scenario, origin) = await AuthorizedOriginAsync();
        await Returns(scenario).ExecuteAsync(Request(origin, 600m), "tester");
        var second = await Returns(scenario).ExecuteAsync(Request(origin, 400m), "tester");
        var context = TestDb.CreateUnitOfWork(scenario.DatabaseName).Context;
        var line = await context.PurchaseInvoicesItems.SingleAsync(i => i.PurchaseInvoiceKey == second.Key);
        line.Quantity = 500m; // gravado por fora, como um dado concorrente
        await context.SaveChangesAsync();
        scenario.Db.Context.ChangeTracker.Clear();
        var reservation = new FakeNfeNumberReservationService(10);

        var e = await Assert.ThrowsAsync<DefaultException>(() =>
            PurchaseInvoicesNfeIssueServiceTests.Issue(scenario, new FakeNfeSefazClient(), reservation).ExecuteAsync(second.Key, "tester"));

        Assert.Equal("Item TRIGO: a devolução (500,000) passa do saldo devolvível da compra (400,000).", e.Message);
        Assert.Equal(0, reservation.Calls);
    }

    [Fact]
    public async Task Editing_the_return_above_the_balance_is_refused()
    {
        var (scenario, origin) = await AuthorizedOriginAsync();
        var created = await Returns(scenario).ExecuteAsync(Request(origin, 400m), "tester");
        var line = await scenario.Db.Context.PurchaseInvoicesItems.AsNoTracking().SingleAsync(i => i.PurchaseInvoiceKey == created.Key);
        line.Quantity = 1000.5m;

        var e = await Assert.ThrowsAsync<DefaultException>(() =>
            new PurchaseInvoicesItemsUpdateService(scenario.Db, new FakeItemService(), TaxTestServices.PurchaseApply(scenario.Db, Partners()))
                .ExecuteAsync(line.Key!.Value, line, "tester"));

        Assert.Equal("Item TRIGO: a devolução (1.000,500) passa do saldo devolvível da compra (1.000,000).", e.Message);
    }

    [Fact]
    public async Task Returnable_items_show_purchased_returned_and_balance()
    {
        var (scenario, origin) = await AuthorizedOriginAsync();
        await Returns(scenario).ExecuteAsync(Request(origin, 250m), "tester");

        var row = Assert.Single(await new PurchaseInvoicesNfeReturnableItemsService(scenario.Db).ExecuteAsync(origin.Key));

        Assert.Equal("TRIGO", row.ItemCode);
        Assert.Equal(1000d, row.PurchasedQuantity);
        Assert.Equal(250d, row.ReturnedQuantity);
        Assert.Equal(750d, row.Returnable);
    }

    [Fact]
    public async Task Return_whose_origin_is_no_longer_confirmed_is_refused_before_the_number()
    {
        // Estorno da entrada feito depois que a devolução nasceu.
        var (scenario, origin) = await AuthorizedOriginAsync();
        var created = await Returns(scenario).ExecuteAsync(Request(origin, 400m), "tester");
        var context = TestDb.CreateUnitOfWork(scenario.DatabaseName).Context;
        var entry = await context.PurchaseInvoices.SingleAsync(i => i.Key == origin.Key);
        entry.InvoiceStatus = InvoiceStatus.Pending;
        await context.SaveChangesAsync();
        scenario.Db.Context.ChangeTracker.Clear();
        var reservation = new FakeNfeNumberReservationService(10);

        var e = await Assert.ThrowsAsync<DefaultException>(() =>
            PurchaseInvoicesNfeIssueServiceTests.Issue(scenario, new FakeNfeSefazClient(), reservation).ExecuteAsync(created.Key, "tester"));

        Assert.Contains("Entrada de origem", e.Message);
        Assert.Contains("não está confirmada", e.Message);
        Assert.Equal(0, reservation.Calls);
    }
}
