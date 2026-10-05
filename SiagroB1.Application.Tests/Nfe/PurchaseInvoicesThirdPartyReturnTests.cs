using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Nfe;
using SiagroB1.Application.Services.PurchaseInvoices;
using SiagroB1.Application.Services.Taxes;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Application.Tests.Nfe;

/// <summary>"Devolver" da entrada de terceiro (spec terceiro §8): guardas, nItem digitado, natureza da filial, conferência.</summary>
public class PurchaseInvoicesThirdPartyReturnTests
{
    private static FakeBusinessPartnerService Partners() => new(
        names: new Dictionary<string, string> { [PurchaseNfeTestSeed.Supplier] = "PRODUTOR RURAL TESTE" },
        states: new Dictionary<string, string> { [PurchaseNfeTestSeed.Supplier] = "SP" });

    internal static PurchaseInvoicesNfeReturnCreateService Returns(PurchaseNfeScenario scenario, FakeBusinessPartnerService? partners = null) =>
        new(scenario.Db, new TaxCalculationGate(scenario.Db, NfeTestSeed.Config()),
            new PurchaseInvoicesCreateService(scenario.Db, partners ?? Partners(), new FakeItemService(), TaxTestServices.PurchaseApply(scenario.Db, partners ?? Partners())),
            NfeTestSeed.Clock, NfeIssueInputAssembler.BrasiliaZone);

    private static PurchaseInvoiceNfeReturnRequest Request(PurchaseInvoice origin, params (string Code, decimal Quantity, int? ItemNumber)[] lines) =>
        new(origin.Key, lines.Select(l => new PurchaseInvoiceNfeReturnItem(
            origin.Items.Single(i => i.ItemCode == l.Code).Key!.Value, l.Quantity, l.ItemNumber)).ToList(), "grão fora do padrão");

    [Fact]
    public async Task Imported_entry_returns_with_the_branch_usage_and_the_supplier_item_numbers()
    {
        var (scenario, origin) = await ThirdPartyPurchaseSeed.SeedAsync();

        var created = await Returns(scenario).ExecuteAsync(Request(origin, ("MILHO", 200m, null)), "tester");

        var saved = await PurchaseInvoicesNfeIssueServiceTests.ReloadAsync(scenario, created.Key);
        Assert.Equal((PurchaseInvoiceType.Return, DocumentIssuerType.Own, true, origin.Key),
            (saved.InvoiceType, saved.IssuerType, saved.IsNfeReturn, saved.PurchaseInvoiceOriginKey!.Value));
        var line = saved.Items.Single();
        Assert.Equal((200m, scenario.ReturnUsage, "5202", "51"), (line.Quantity, line.UsageCode!.Value, line.Cfop, line.CstIcms));
        Assert.StartsWith("Devolução da NF-e 456 série 1. Motivo: grão fora do padrão", saved.Comments);
    }

    [Theory]
    [InlineData("absent")]
    [InlineData("inactive")]
    [InlineData("incoming")]
    public async Task Branch_without_a_valid_usage_is_refused(string kind)
    {
        var (scenario, origin) = await ThirdPartyPurchaseSeed.SeedAsync(configureUsage: kind != "absent");
        var context = scenario.Db.Context;
        if (kind == "inactive")
            (await context.Usages.SingleAsync(u => u.Code == scenario.ReturnUsage)).Inactive = true;
        else if (kind == "incoming")
            (await context.Branchs.SingleAsync(b => b.Code == "01")).ThirdPartyPurchaseReturnUsageCode =
                (await context.Usages.FirstAsync(u => u.Direction == UsageDirection.Incoming)).Code;
        await scenario.Db.SaveChangesAsync();
        context.ChangeTracker.Clear();

        var e = await Assert.ThrowsAsync<DefaultException>(() => Returns(scenario).ExecuteAsync(Request(origin, ("TRIGO", 1m, null)), "tester"));

        Assert.Equal("Configure a natureza de devolução de compra de terceiro na filial 01.", e.Message);
    }

    [Theory]
    [InlineData("pending")]
    [InlineData("keyless")]
    public async Task Pending_or_keyless_third_party_entry_is_refused(string kind)
    {
        var (scenario, origin) = await ThirdPartyPurchaseSeed.SeedAsync();
        var stored = await scenario.Db.Context.PurchaseInvoices.SingleAsync(i => i.Key == origin.Key);
        if (kind == "pending")
            stored.InvoiceStatus = InvoiceStatus.Pending;
        else
            stored.ChaveNFe = null;
        await scenario.Db.SaveChangesAsync();
        scenario.Db.Context.ChangeTracker.Clear();

        var e = await Assert.ThrowsAsync<DefaultException>(() => Returns(scenario).ExecuteAsync(Request(origin, ("TRIGO", 1m, null)), "tester"));

        Assert.Equal("A devolução de entrada de terceiro parte de um documento Normal, confirmado e com a chave da NF-e do fornecedor (44 dígitos).", e.Message);
    }

    [Fact]
    public async Task Third_party_entry_of_another_document_kind_is_refused()
    {
        // Só a NF-e do fornecedor é referenciada pela devolução; "outro documento" não tem chave de NF-e.
        var (scenario, origin) = await ThirdPartyPurchaseSeed.SeedAsync();
        (await scenario.Db.Context.PurchaseInvoices.SingleAsync(i => i.Key == origin.Key)).TaxDocumentKind = TaxDocumentKind.Other;
        await scenario.Db.SaveChangesAsync();
        scenario.Db.Context.ChangeTracker.Clear();

        var e = await Assert.ThrowsAsync<DefaultException>(() => Returns(scenario).ExecuteAsync(Request(origin, ("TRIGO", 1m, null)), "tester"));

        Assert.Equal("A devolução de entrada de terceiro parte de um documento Normal, confirmado e com a chave da NF-e do fornecedor (44 dígitos).", e.Message);
    }

    [Fact]
    public async Task Total_return_creates_both_lines_with_the_branch_usage()
    {
        var (scenario, origin) = await ThirdPartyPurchaseSeed.SeedAsync();

        var created = await Returns(scenario).ExecuteAsync(Request(origin, ("TRIGO", 1000m, null), ("MILHO", 500m, null)), "tester");

        var saved = await PurchaseInvoicesNfeIssueServiceTests.ReloadAsync(scenario, created.Key);
        Assert.Equal(2, saved.Items.Count);
        Assert.Equal(new[] { 1000m, 500m }, saved.Items.Select(i => i.Quantity).OrderByDescending(q => q).ToArray());
        Assert.All(saved.Items, i => Assert.Equal(scenario.ReturnUsage, i.UsageCode));
    }

    [Fact]
    public async Task Typed_entry_requires_the_item_number()
    {
        var (scenario, origin) = await ThirdPartyPurchaseSeed.SeedAsync(withXml: false);

        var e = await Assert.ThrowsAsync<DefaultException>(() => Returns(scenario).ExecuteAsync(Request(origin, ("TRIGO", 1m, null)), "tester"));

        Assert.Equal("Item TRIGO: informe o número do item na NF-e do fornecedor.", e.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(991)]
    public async Task Item_number_out_of_range_is_refused(int typed)
    {
        var (scenario, origin) = await ThirdPartyPurchaseSeed.SeedAsync(withXml: false);

        var e = await Assert.ThrowsAsync<DefaultException>(() => Returns(scenario).ExecuteAsync(Request(origin, ("TRIGO", 1m, typed)), "tester"));

        Assert.Equal("Item TRIGO: informe o número do item na NF-e do fornecedor.", e.Message);
    }

    [Fact]
    public async Task Typed_item_number_already_used_is_refused()
    {
        var (scenario, origin) = await ThirdPartyPurchaseSeed.SeedAsync(withXml: false);
        var milho = await scenario.Db.Context.PurchaseInvoicesItems.SingleAsync(i => i.ItemCode == "MILHO" && i.PurchaseInvoiceKey == origin.Key);
        milho.NfeItemNumber = 2;
        await scenario.Db.SaveChangesAsync();
        scenario.Db.Context.ChangeTracker.Clear();
        origin = await scenario.Db.Context.PurchaseInvoices.AsNoTracking().Include(i => i.Items).SingleAsync(i => i.Key == origin.Key);

        var e = await Assert.ThrowsAsync<DefaultException>(() => Returns(scenario).ExecuteAsync(Request(origin, ("TRIGO", 1m, 2)), "tester"));

        Assert.Equal("Item TRIGO: o número 2 já é de outro item desta NF-e do fornecedor.", e.Message);
    }

    [Theory]
    [InlineData(9, false)]
    [InlineData(1, true)]
    public async Task Typed_item_number_must_exist_in_the_stored_xml(int typed, bool accepted)
    {
        var (scenario, origin) = await ThirdPartyPurchaseSeed.SeedAsync();
        var trigo = await scenario.Db.Context.PurchaseInvoicesItems.SingleAsync(i => i.ItemCode == "TRIGO" && i.PurchaseInvoiceKey == origin.Key);
        trigo.NfeItemNumber = null;
        await scenario.Db.SaveChangesAsync();
        scenario.Db.Context.ChangeTracker.Clear();
        origin = await scenario.Db.Context.PurchaseInvoices.AsNoTracking().Include(i => i.Items).SingleAsync(i => i.Key == origin.Key);

        var run = () => Returns(scenario).ExecuteAsync(Request(origin, ("TRIGO", 100m, typed)), "tester");

        if (accepted)
            Assert.NotEqual(Guid.Empty, (await run()).Key);
        else
            Assert.Equal("Item TRIGO: o item 9 não existe na NF-e do fornecedor.",
                (await Assert.ThrowsAsync<DefaultException>(run)).Message);
    }

    [Fact]
    public async Task Typed_item_number_is_saved_and_reused_by_the_next_return()
    {
        var (scenario, origin) = await ThirdPartyPurchaseSeed.SeedAsync(withXml: false);

        await Returns(scenario).ExecuteAsync(Request(origin, ("TRIGO", 100m, 3)), "tester");
        scenario.Db.Context.ChangeTracker.Clear();
        var reloaded = await scenario.Db.Context.PurchaseInvoices.AsNoTracking().Include(i => i.Items).SingleAsync(i => i.Key == origin.Key);
        Assert.Equal(3, reloaded.Items.Single(i => i.ItemCode == "TRIGO").NfeItemNumber);

        // Na segunda o número já está na linha: um ItemNumber enviado é ignorado.
        var second = await Returns(scenario).ExecuteAsync(Request(reloaded, ("TRIGO", 100m, 7)), "tester");

        Assert.NotEqual(Guid.Empty, second.Key);
        scenario.Db.Context.ChangeTracker.Clear();
        Assert.Equal(3, (await scenario.Db.Context.PurchaseInvoicesItems.AsNoTracking().SingleAsync(i => i.ItemCode == "TRIGO" && i.PurchaseInvoiceKey == origin.Key)).NfeItemNumber);
    }

    [Fact]
    public async Task Typed_line_returns_without_the_conference()
    {
        var (scenario, origin) = await ThirdPartyPurchaseSeed.SeedAsync(withXml: false);

        var created = await Returns(scenario).ExecuteAsync(Request(origin, ("TRIGO", 100m, 1)), "tester");

        Assert.Equal("51", (await PurchaseInvoicesNfeIssueServiceTests.ReloadAsync(scenario, created.Key)).Items.Single().CstIcms);
    }

    [Fact]
    public async Task Supplier_taxation_the_usage_does_not_mirror_is_refused()
    {
        var (scenario, origin) = await ThirdPartyPurchaseSeed.SeedAsync(icms: SupplierNfeXml.Icms00(1500m, 18m));

        var e = await Assert.ThrowsAsync<DefaultException>(() => Returns(scenario).ExecuteAsync(Request(origin, ("TRIGO", 1m, null)), "tester"));

        Assert.Contains("Item TRIGO: a natureza de devolução DEVOLUCAO DE COMPRA não reproduz a tributação da compra — CST do ICMS: compra 00, devolução 51", e.Message);
    }

    [Theory]
    [InlineData("st")]
    [InlineData("ipi")]
    public async Task Item_with_ipi_or_st_is_refused(string kind)
    {
        var icms = kind == "st" ? SupplierNfeXml.Icms10WithSt() : SupplierNfeXml.Icms51(1500m);
        var (scenario, origin) = await ThirdPartyPurchaseSeed.SeedAsync(icms: icms);
        if (kind == "ipi")
        {
            var stored = await scenario.Db.Context.PurchaseInvoices.SingleAsync(i => i.Key == origin.Key);
            stored.XmlData = SupplierNfeXml.Bytes(SupplierNfeXml.Build(
                SupplierNfeXml.Det(1, "TRIGO", "TRIGO EM GRAOS", 1000m, 1.5m, SupplierNfeXml.Icms51(1500m), ipi: SupplierNfeXml.Ipi(10m)),
                SupplierNfeXml.Det(2, "MILHO", "MILHO EM GRAOS", 500m, 1m, SupplierNfeXml.Icms51(500m))));
            await scenario.Db.SaveChangesAsync();
            scenario.Db.Context.ChangeTracker.Clear();
        }

        var e = await Assert.ThrowsAsync<DefaultException>(() => Returns(scenario).ExecuteAsync(Request(origin, ("TRIGO", 1m, null)), "tester"));

        Assert.Equal("Item TRIGO: a nota do fornecedor tem IPI/ICMS-ST neste item, que o Siagro ainda não devolve.", e.Message);
    }

    [Fact]
    public async Task Line_whose_product_is_not_registered_is_refused()
    {
        var (scenario, origin) = await ThirdPartyPurchaseSeed.SeedAsync();
        var line = await scenario.Db.Context.PurchaseInvoicesItems.SingleAsync(i => i.ItemCode == "TRIGO" && i.PurchaseInvoiceKey == origin.Key);
        line.ItemCode = "TRG-FORNECEDOR";
        await scenario.Db.SaveChangesAsync();
        scenario.Db.Context.ChangeTracker.Clear();
        origin = await scenario.Db.Context.PurchaseInvoices.AsNoTracking().Include(i => i.Items).SingleAsync(i => i.Key == origin.Key);

        var e = await Assert.ThrowsAsync<DefaultException>(() => Returns(scenario).ExecuteAsync(Request(origin, ("TRG-FORNECEDOR", 1m, null)), "tester"));

        Assert.Equal("Item TRG-FORNECEDOR: o produto não está cadastrado; ajuste o produto na entrada antes de devolver.", e.Message);
    }

    [Fact]
    public async Task Returnable_items_show_the_supplier_item_number()
    {
        var (scenario, origin) = await ThirdPartyPurchaseSeed.SeedAsync();

        var items = await new PurchaseInvoicesNfeReturnableItemsService(scenario.Db).ExecuteAsync(origin.Key);

        Assert.Equal(new[] { 1, 2 }, items.Select(i => i.ItemNumber!.Value).ToArray());
    }

    [Fact]
    public async Task Own_entry_return_ignores_item_numbers()
    {
        var scenario = await PurchaseNfeTestSeed.SeedAsync();
        var sefaz = new FakeNfeSefazClient();
        sefaz.AuthorizeResponses.Enqueue(key => FakeNfeSefazClient.Authorized(key));
        await PurchaseInvoicesNfeIssueServiceTests.Issue(scenario, sefaz).ExecuteAsync(scenario.InvoiceKey, "tester");
        scenario.Db.Context.ChangeTracker.Clear();
        var origin = await PurchaseInvoicesNfeIssueServiceTests.ReloadAsync(scenario);

        var created = await Returns(scenario).ExecuteAsync(
            new PurchaseInvoiceNfeReturnRequest(origin.Key, [new PurchaseInvoiceNfeReturnItem(origin.Items.Single().Key!.Value, 400m, 77)], "x"), "tester");

        Assert.Equal(400m, (await PurchaseInvoicesNfeIssueServiceTests.ReloadAsync(scenario, created.Key)).Items.Single().Quantity);
        Assert.Equal(1, (await PurchaseInvoicesNfeIssueServiceTests.ReloadAsync(scenario)).Items.Single().NfeItemNumber);
    }

    [Theory]
    [InlineData("TRIGO", 1, "5202")]
    [InlineData("MILHO", 2, "5202")]
    public async Task Third_party_return_is_issued_referencing_the_supplier_item(string code, int nItem, string cfop)
    {
        var (scenario, origin) = await ThirdPartyPurchaseSeed.SeedAsync();
        var created = await Returns(scenario).ExecuteAsync(Request(origin, (code, 100m, null)), "tester");
        var sefaz = new FakeNfeSefazClient();
        sefaz.AuthorizeResponses.Enqueue(key => FakeNfeSefazClient.Authorized(key));

        var outcome = await PurchaseInvoicesNfeIssueServiceTests.Issue(scenario, sefaz).ExecuteAsync(created.Key, "tester");

        Assert.Equal(NfeStatus.Authorized, outcome.NfeStatus);
        var signed = Assert.Single(sefaz.Sent).Xml;
        Assert.Contains("<tpNF>1</tpNF>", signed);
        Assert.Contains("<finNFe>4</finNFe>", signed);
        Assert.Contains($"<CFOP>{cfop}</CFOP>", signed);
        Assert.Contains($"<chaveAcesso>{SupplierNfeXml.AccessKey}</chaveAcesso>", signed);
        Assert.Contains($"<nItem>{nItem}</nItem>", signed);
        Assert.DoesNotContain("<NFref>", signed);
        Assert.Contains("<tPag>90</tPag>", signed);
        Assert.Contains("<CPF>52998224725</CPF>", signed);
        Assert.Contains("Devolução da NF-e nº 456, série 1", signed);
    }

    [Fact]
    public async Task Typed_entry_calculated_by_the_engine_is_conferred_and_mirrored()
    {
        var (scenario, origin) = await ThirdPartyPurchaseSeed.SeedAsync(withXml: false);
        var context = scenario.Db.Context;
        var usage = await PurchaseNfeTestSeed.PurchaseUsageCodeAsync(scenario.Db);
        var stored = await context.PurchaseInvoices.Include(i => i.Items).SingleAsync(i => i.Key == origin.Key);
        stored.InvoiceStatus = InvoiceStatus.Pending;
        foreach (var item in stored.Items)
            item.UsageCode = usage;
        await TaxTestServices.PurchaseApply(scenario.Db, PurchaseNfeTestSeed.Partners()).ApplyAsync(stored, stored.Items);
        stored.InvoiceStatus = InvoiceStatus.Confirmed;
        await scenario.Db.SaveChangesAsync();
        context.ChangeTracker.Clear();
        origin = await context.PurchaseInvoices.AsNoTracking().Include(i => i.Items).SingleAsync(i => i.Key == origin.Key);
        Assert.Equal("51", origin.Items.Single(i => i.ItemCode == "TRIGO").CstIcms);

        var created = await Returns(scenario).ExecuteAsync(Request(origin, ("TRIGO", 100m, 1)), "tester");

        var line = (await PurchaseInvoicesNfeIssueServiceTests.ReloadAsync(scenario, created.Key)).Items.Single();
        Assert.Equal(("5202", "51", 100m), (line.Cfop, line.CstIcms, line.IcmsDeferral));
    }

    [Fact]
    public async Task Third_party_return_from_a_supplier_out_of_state_is_issued_with_cfop_6202()
    {
        var (scenario, origin) = await ThirdPartyPurchaseSeed.SeedAsync();
        var context = scenario.Db.Context;

        // Fornecedor do PR: a natureza de devolução usa o ICMS fora do estado (CST 00) e a alíquota interestadual de 12%.
        context.Municipalities.Add(new Municipality { Code = "4106902", Name = "Curitiba", StateAbbreviation = "PR" });
        var address = await context.Addresses.SingleAsync(a => a.CardCode == PurchaseNfeTestSeed.Supplier);
        (address.State, address.City, address.MunicipalityCode) = ("PR", "Curitiba", "4106902");
        var stored = await context.PurchaseInvoices.Include(i => i.Items).SingleAsync(i => i.Key == origin.Key);
        stored.XmlData = SupplierNfeXml.Bytes(SupplierNfeXml.Build(
            SupplierNfeXml.Det(1, "TRIGO", "TRIGO EM GRAOS", 1000m, 1.5m, SupplierNfeXml.Icms00(1500m, 12m), cfop: "6102", benefitCode: null),
            SupplierNfeXml.Det(2, "MILHO", "MILHO EM GRAOS", 500m, 1m, SupplierNfeXml.Icms00(500m, 12m), cfop: "6102", benefitCode: null)));
        LegacySupplierSnapshot.Apply(stored);
        await scenario.Db.SaveChangesAsync();
        context.ChangeTracker.Clear();
        origin = await context.PurchaseInvoices.AsNoTracking().Include(i => i.Items).SingleAsync(i => i.Key == origin.Key);
        var partners = new FakeBusinessPartnerService(
            names: new Dictionary<string, string> { [PurchaseNfeTestSeed.Supplier] = "PRODUTOR RURAL TESTE" },
            states: new Dictionary<string, string> { [PurchaseNfeTestSeed.Supplier] = "PR" });

        var created = await Returns(scenario, partners).ExecuteAsync(Request(origin, ("TRIGO", 100m, null)), "tester");
        var sefaz = new FakeNfeSefazClient();
        sefaz.AuthorizeResponses.Enqueue(key => FakeNfeSefazClient.Authorized(key));

        var outcome = await PurchaseInvoicesNfeIssueServiceTests.Issue(scenario, sefaz).ExecuteAsync(created.Key, "tester");

        Assert.Equal(NfeStatus.Authorized, outcome.NfeStatus);
        var signed = Assert.Single(sefaz.Sent).Xml;
        Assert.Contains("<CFOP>6202</CFOP>", signed);
        Assert.Contains("<idDest>2</idDest>", signed);
    }

    [Fact]
    public async Task Return_whose_origin_lost_its_key_is_refused_before_the_number()
    {
        var (scenario, origin) = await ThirdPartyPurchaseSeed.SeedAsync();
        var created = await Returns(scenario).ExecuteAsync(Request(origin, ("TRIGO", 100m, null)), "tester");
        var context = TestDb.CreateUnitOfWork(scenario.DatabaseName).Context;
        (await context.PurchaseInvoices.SingleAsync(i => i.Key == origin.Key)).ChaveNFe = null;
        await context.SaveChangesAsync();
        scenario.Db.Context.ChangeTracker.Clear();
        var reservation = new FakeNfeNumberReservationService(10);

        var e = await Assert.ThrowsAsync<DefaultException>(() =>
            PurchaseInvoicesNfeIssueServiceTests.Issue(scenario, new FakeNfeSefazClient(), reservation).ExecuteAsync(created.Key, "tester"));

        Assert.Contains("Entrada de origem 456: sem a chave da NF-e do fornecedor", e.Message);
        Assert.Equal(0, reservation.Calls);
    }
}
