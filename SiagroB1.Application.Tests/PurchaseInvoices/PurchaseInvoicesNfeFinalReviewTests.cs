using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Nfe;
using SiagroB1.Application.Services.PurchaseInvoices;
using SiagroB1.Application.Services.Taxes;
using SiagroB1.Application.Tests.Nfe;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Application.Tests.PurchaseInvoices;

/// <summary>Correções da revisão final do backend da NF-e de entrada própria e da devolução de compra.</summary>
public class PurchaseInvoicesNfeFinalReviewTests
{
    private static FakeBusinessPartnerService Partners() => new(
        names: new Dictionary<string, string> { [PurchaseNfeTestSeed.Supplier] = "PRODUTOR RURAL TESTE" },
        states: new Dictionary<string, string> { [PurchaseNfeTestSeed.Supplier] = "SP" });

    private static PurchaseInvoicesUpdateService HeaderUpdate(PurchaseNfeScenario s) =>
        new(s.Db, Partners(), new FakeItemService(), TaxTestServices.PurchaseApply(s.Db, Partners()));

    private static PurchaseInvoicesItemsUpdateService LineUpdate(PurchaseNfeScenario s) =>
        new(s.Db, new FakeItemService(), TaxTestServices.PurchaseApply(s.Db, Partners()));

    private static PurchaseInvoicesNfeReturnCreateService Returns(PurchaseNfeScenario s) =>
        new(s.Db, new TaxCalculationGate(s.Db, NfeTestSeed.Config()),
            new PurchaseInvoicesCreateService(s.Db, Partners(), new FakeItemService(), TaxTestServices.PurchaseApply(s.Db, Partners()), new FakeDocNumberSequenceService()),
            NfeTestSeed.Clock, NfeIssueInputAssembler.BrasiliaZone);

    /// <summary>Entrada emitida e autorizada pela emissão, depois estornada (Pendente com NF-e Autorizada).</summary>
    private static async Task<PurchaseNfeScenario> AuthorizedAndReversedAsync()
    {
        var scenario = await PurchaseNfeTestSeed.SeedAsync();
        var sefaz = new FakeNfeSefazClient();
        sefaz.AuthorizeResponses.Enqueue(key => FakeNfeSefazClient.Authorized(key));
        await PurchaseInvoicesNfeIssueServiceTests.Issue(scenario, sefaz).ExecuteAsync(scenario.InvoiceKey, "tester");
        // O estado vem de uma confirmação que falhou depois da autorização (o Estornar não aceita mais NF-e autorizada).
        var invoice = await scenario.Db.Context.PurchaseInvoices.SingleAsync(i => i.Key == scenario.InvoiceKey);
        invoice.InvoiceStatus = InvoiceStatus.Pending;
        invoice.ApprovedAt = null;
        invoice.ApprovedBy = null;
        await scenario.Db.SaveChangesAsync();
        scenario.Db.Context.ChangeTracker.Clear();
        return scenario;
    }

    private static async Task ChangeNatureAsync(PurchaseNfeScenario s, Action<Usage> change)
    {
        var usage = await s.Db.Context.Usages.SingleAsync(u => u.Direction == UsageDirection.Incoming);
        change(usage);
        await s.Db.SaveChangesAsync();
        s.Db.Context.ChangeTracker.Clear();
    }

    [Fact]
    public async Task Header_save_of_an_authorized_and_reversed_entry_does_not_recalculate()
    {
        var s = await AuthorizedAndReversedAsync();
        await ChangeNatureAsync(s, u => u.IcmsInStateRate = 12m);
        var changed = await PurchaseInvoicesNfeIssueServiceTests.ReloadAsync(s);
        changed.Comments = "só um comentário";

        await HeaderUpdate(s).ExecuteAsync(s.InvoiceKey, changed, "tester");

        var saved = await PurchaseInvoicesNfeIssueServiceTests.ReloadAsync(s);
        Assert.Equal("só um comentário", saved.Comments);
        Assert.Equal(18m, saved.Items.Single().IcmsRate);
    }

    [Fact]
    public async Task Line_save_of_an_authorized_and_reversed_entry_does_not_recalculate()
    {
        var s = await AuthorizedAndReversedAsync();
        await ChangeNatureAsync(s, u => u.IcmsInStateRate = 12m);
        var line = (await PurchaseInvoicesNfeIssueServiceTests.ReloadAsync(s)).Items.Single();

        await LineUpdate(s).ExecuteAsync(line.Key!.Value, line, "tester");

        var saved = (await PurchaseInvoicesNfeIssueServiceTests.ReloadAsync(s)).Items.Single();
        Assert.Equal(18m, saved.IcmsRate);
    }

    [Fact]
    public async Task Inactive_nature_does_not_block_a_comment_on_an_authorized_and_reversed_entry()
    {
        var s = await AuthorizedAndReversedAsync();
        await ChangeNatureAsync(s, u => u.Inactive = true);
        var changed = await PurchaseInvoicesNfeIssueServiceTests.ReloadAsync(s);
        changed.Comments = "ainda grava";

        await HeaderUpdate(s).ExecuteAsync(s.InvoiceKey, changed, "tester");

        Assert.Equal("ainda grava", (await PurchaseInvoicesNfeIssueServiceTests.ReloadAsync(s)).Comments);
    }

    private static async Task<(PurchaseNfeScenario Scenario, Guid ReturnKey, Guid LineKey)> PendingReturnAsync()
    {
        var scenario = await PurchaseNfeTestSeed.SeedAsync();
        var sefaz = new FakeNfeSefazClient();
        sefaz.AuthorizeResponses.Enqueue(key => FakeNfeSefazClient.Authorized(key));
        await PurchaseInvoicesNfeIssueServiceTests.Issue(scenario, sefaz).ExecuteAsync(scenario.InvoiceKey, "tester");
        scenario.Db.Context.ChangeTracker.Clear();
        var origin = await PurchaseInvoicesNfeIssueServiceTests.ReloadAsync(scenario);
        var created = await Returns(scenario).ExecuteAsync(
            new PurchaseInvoiceNfeReturnRequest(origin.Key, [new PurchaseInvoiceNfeReturnItem(origin.Items.Single().Key!.Value, 400m)], "grão fora do padrão"),
            "tester");
        scenario.Db.Context.ChangeTracker.Clear();
        var line = (await PurchaseInvoicesNfeIssueServiceTests.ReloadAsync(scenario, created.Key)).Items.Single();
        return (scenario, created.Key, line.Key!.Value);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task Purchase_return_line_quantity_must_be_positive(int quantity)
    {
        var (s, _, lineKey) = await PendingReturnAsync();
        var line = await s.Db.Context.PurchaseInvoicesItems.AsNoTracking().SingleAsync(i => i.Key == lineKey);
        line.Quantity = quantity;

        var e = await Assert.ThrowsAsync<DefaultException>(() => LineUpdate(s).ExecuteAsync(lineKey, line, "tester"));

        Assert.Equal("Item TRIGO: informe a quantidade a devolver.", e.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task Purchase_return_header_save_requires_a_positive_quantity(int quantity)
    {
        var (s, returnKey, _) = await PendingReturnAsync();
        var changed = await PurchaseInvoicesNfeIssueServiceTests.ReloadAsync(s, returnKey);
        changed.Items.Single().Quantity = quantity;

        var e = await Assert.ThrowsAsync<DefaultException>(() => HeaderUpdate(s).ExecuteAsync(returnKey, changed, "tester"));

        Assert.Equal("Item TRIGO: informe a quantidade a devolver.", e.Message);
    }

    [Fact]
    public async Task Saving_an_entry_without_a_snapshot_fills_it()
    {
        // Review Focus 2 (segunda metade): entrada criada com a chave desligada e salva depois de ligá-la.
        var s = await PurchaseNfeTestSeed.SeedAsync();
        var context = s.Db.Context;
        var stored = await context.PurchaseInvoices.Include(i => i.Items).SingleAsync(i => i.Key == s.InvoiceKey);
        stored.Items.Single().Cfop = null;
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var changed = await PurchaseInvoicesNfeIssueServiceTests.ReloadAsync(s);

        await HeaderUpdate(s).ExecuteAsync(s.InvoiceKey, changed, "tester");

        Assert.Equal("1102", (await PurchaseInvoicesNfeIssueServiceTests.ReloadAsync(s)).Items.Single().Cfop);
    }
}
