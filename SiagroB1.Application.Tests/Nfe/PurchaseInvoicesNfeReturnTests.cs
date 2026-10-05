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
    public async Task Return_copies_the_volume_of_the_entry()
    {
        var (scenario, origin) = await AuthorizedOriginAsync();
        var stored = await scenario.Db.Context.PurchaseInvoices.SingleAsync(i => i.Key == origin.Key);
        (stored.VolumeQuantity, stored.VolumeSpecies, stored.VolumeBrand, stored.VolumeNumbering) = (20, "SACO", "CEAGUI", "1 A 20");
        await scenario.Db.SaveChangesAsync();
        scenario.Db.Context.ChangeTracker.Clear();

        var created = await Returns(scenario).ExecuteAsync(Request(origin, 400m), "tester");

        var saved = await PurchaseInvoicesNfeIssueServiceTests.ReloadAsync(scenario, created.Key);
        Assert.Equal((20, "SACO", "CEAGUI", "1 A 20"),
            (saved.VolumeQuantity, saved.VolumeSpecies, saved.VolumeBrand, saved.VolumeNumbering));
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
    public async Task Editing_the_return_checks_the_discount_against_the_restored_price()
    {
        // Revisão final M2: o preço enviado (50) é descartado pela trava; o desconto que só cabe nele não pode passar.
        var (scenario, origin) = await AuthorizedOriginAsync();
        var created = await Returns(scenario).ExecuteAsync(Request(origin, 400m), "tester");
        var line = await scenario.Db.Context.PurchaseInvoicesItems.AsNoTracking().SingleAsync(i => i.PurchaseInvoiceKey == created.Key);
        line.UnitPrice = 50m;
        line.DiscountValue = 10000m;

        var e = await Assert.ThrowsAsync<DefaultException>(() =>
            new PurchaseInvoicesItemsUpdateService(scenario.Db, new FakeItemService(), TaxTestServices.PurchaseApply(scenario.Db, Partners()))
                .ExecuteAsync(line.Key!.Value, line, "tester"));

        Assert.Equal("Item TRIGO: o desconto passa do valor da linha.", e.Message);
    }

    [Fact]
    public async Task Editing_the_return_through_the_document_checks_the_discount_against_the_restored_price()
    {
        var (scenario, origin) = await AuthorizedOriginAsync();
        var created = await Returns(scenario).ExecuteAsync(Request(origin, 400m), "tester");
        var saved = await PurchaseInvoicesNfeIssueServiceTests.ReloadAsync(scenario, created.Key);
        var line = saved.Items.Single();
        line.UnitPrice = 50m;
        line.DiscountValue = 10000m;
        var incoming = new PurchaseInvoice { CardCode = saved.CardCode };
        incoming.AddItem(line);

        var e = await Assert.ThrowsAsync<DefaultException>(() =>
            new PurchaseInvoicesUpdateService(scenario.Db, Partners(), new FakeItemService(), TaxTestServices.PurchaseApply(scenario.Db, Partners()))
                .ExecuteAsync(created.Key, incoming, "tester"));

        Assert.Equal("Item TRIGO: o desconto passa do valor da linha.", e.Message);
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

    // --- Frete, seguro, desconto e outras despesas na proporção do que volta (spec 2026-10-05 D4) ---

    /// <summary>A entrada do cenário com os quatro valores na linha, autorizada e confirmada pela emissão.</summary>
    private static async Task<(PurchaseNfeScenario Scenario, PurchaseInvoice Origin)> AuthorizedOriginWithChargesAsync(
        decimal freight, decimal insurance, decimal discount, decimal other)
    {
        var scenario = await PurchaseNfeTestSeed.SeedAsync();
        var line = await scenario.Db.Context.PurchaseInvoicesItems.SingleAsync(i => i.PurchaseInvoiceKey == scenario.InvoiceKey);
        (line.FreightValue, line.InsuranceValue, line.DiscountValue, line.OtherExpensesValue) = (freight, insurance, discount, other);
        await scenario.Db.SaveChangesAsync();
        var sefaz = new FakeNfeSefazClient();
        sefaz.AuthorizeResponses.Enqueue(key => FakeNfeSefazClient.Authorized(key));
        await PurchaseInvoicesNfeIssueServiceTests.Issue(scenario, sefaz).ExecuteAsync(scenario.InvoiceKey, "tester");
        scenario.Db.Context.ChangeTracker.Clear();

        return (scenario, await PurchaseInvoicesNfeIssueServiceTests.ReloadAsync(scenario));
    }

    [Fact]
    public async Task Partial_purchase_return_brings_the_charges_in_proportion_and_taxes_them()
    {
        var (scenario, origin) = await AuthorizedOriginWithChargesAsync(90m, 0.05m, 30m, 0m);

        var created = await Returns(scenario).ExecuteAsync(Request(origin, 400m), "tester");

        var line = (await PurchaseInvoicesNfeIssueServiceTests.ReloadAsync(scenario, created.Key)).Items.Single();
        Assert.Equal((36m, 0.02m, 12m, 0m), (line.FreightValue, line.InsuranceValue, line.DiscountValue, line.OtherExpensesValue));
        // Base = 600,00 + 36,00 + 0,02 − 12,00 (D3 na devolução).
        Assert.Equal(624.02m, line.IcmsBase);
    }

    [Fact]
    public async Task Proportional_discount_never_passes_the_ceiling_of_the_return_line()
    {
        // Revisão final I1: ver o teste equivalente da devolução de venda (3 x 150,125, desconto 450,38, volta 1).
        var scenario = await PurchaseNfeTestSeed.SeedAsync();
        var sefaz = new FakeNfeSefazClient();
        sefaz.AuthorizeResponses.Enqueue(key => FakeNfeSefazClient.Authorized(key));
        await PurchaseInvoicesNfeIssueServiceTests.Issue(scenario, sefaz).ExecuteAsync(scenario.InvoiceKey, "tester");
        scenario.Db.Context.ChangeTracker.Clear();
        // Valores gravados depois da emissão: uma nota inteira bonificada não autoriza no teste (total 0 com pagamento).
        var seeded = await scenario.Db.Context.PurchaseInvoicesItems.SingleAsync(i => i.PurchaseInvoiceKey == scenario.InvoiceKey);
        (seeded.Quantity, seeded.UnitPrice, seeded.DiscountValue) = (3m, 150.125m, 450.38m);
        await scenario.Db.SaveChangesAsync();
        scenario.Db.Context.ChangeTracker.Clear();
        var origin = await PurchaseInvoicesNfeIssueServiceTests.ReloadAsync(scenario);

        var created = await Returns(scenario).ExecuteAsync(Request(origin, 1m), "tester");

        var line = (await PurchaseInvoicesNfeIssueServiceTests.ReloadAsync(scenario, created.Key)).Items.Single();
        Assert.Equal(150.12m, line.Total);
        Assert.Equal(150.12m, line.DiscountValue);
    }

    [Fact]
    public async Task Half_cent_of_the_purchase_return_proportion_rounds_away_from_zero()
    {
        // Review Focus 2.
        var (scenario, origin) = await AuthorizedOriginWithChargesAsync(0m, 0.05m, 0m, 0m);

        var created = await Returns(scenario).ExecuteAsync(Request(origin, 500m), "tester");

        Assert.Equal(0.03m, (await PurchaseInvoicesNfeIssueServiceTests.ReloadAsync(scenario, created.Key)).Items.Single().InsuranceValue);
    }

    [Fact]
    public async Task Total_purchase_return_brings_exactly_the_charges_of_the_entry()
    {
        var (scenario, origin) = await AuthorizedOriginWithChargesAsync(90m, 0.05m, 30m, 7m);

        var created = await Returns(scenario).ExecuteAsync(Request(origin, 1000m), "tester");

        var line = (await PurchaseInvoicesNfeIssueServiceTests.ReloadAsync(scenario, created.Key)).Items.Single();
        Assert.Equal((90m, 0.05m, 30m, 7m), (line.FreightValue, line.InsuranceValue, line.DiscountValue, line.OtherExpensesValue));
    }
}
