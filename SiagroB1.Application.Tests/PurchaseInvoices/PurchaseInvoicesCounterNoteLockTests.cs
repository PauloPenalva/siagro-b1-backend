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

/// <summary>Travas da contra-nota e da escrituração com contra-nota (spec 2026-10-10 §7, D9, D10).</summary>
public class PurchaseInvoicesCounterNoteLockTests
{
    private const string LinesMessage =
        "A contra-nota tem os mesmos itens da escrituração: não é possível incluir nem excluir item.";

    private static async Task<(PurchaseNfeScenario Scenario, PurchaseInvoice Producer, PurchaseInvoice Note)> SeedAsync()
    {
        var (scenario, producer) = await CounterNoteSeed.SeedAsync();
        var created = await PurchaseInvoicesCounterNoteTests.Service(scenario).ExecuteAsync(producer.Key, "tester");
        scenario.Db.Context.ChangeTracker.Clear();

        return (scenario, producer, await PurchaseInvoicesNfeIssueServiceTests.ReloadAsync(scenario, created.Key));
    }

    private static PurchaseInvoicesTaxApplyService Apply(PurchaseNfeScenario scenario) =>
        TaxTestServices.PurchaseApply(scenario.Db, PurchaseNfeTestSeed.Partners());

    [Fact]
    public async Task Update_keeps_what_came_from_the_producer_and_saves_the_transport()
    {
        var (scenario, _, note) = await SeedAsync();
        var incoming = await PurchaseInvoicesNfeIssueServiceTests.ReloadAsync(scenario, note.Key);
        var originalCondition = incoming.PaymentConditionCode;
        incoming.CardCode = "OUTRO";
        incoming.PaymentConditionCode = 999;
        incoming.GrossWeight = 2000m;
        incoming.NetWeight = 1900m;
        var trigo = incoming.Items.Single(i => i.ItemCode == "TRIGO");
        trigo.Quantity = 1m;
        trigo.UnitPrice = 9m;

        await new PurchaseInvoicesUpdateService(scenario.Db, PurchaseNfeTestSeed.Partners(), new FakeItemService(), Apply(scenario))
            .ExecuteAsync(note.Key, incoming, "tester");

        var saved = await PurchaseInvoicesNfeIssueServiceTests.ReloadAsync(scenario, note.Key);
        Assert.Equal((PurchaseNfeTestSeed.Supplier, originalCondition), (saved.CardCode, saved.PaymentConditionCode));
        Assert.Equal((2000m, 1900m), (saved.GrossWeight, saved.NetWeight));
        var line = saved.Items.Single(i => i.ItemCode == "TRIGO");
        Assert.Equal((1000m, 1.5m), (line.Quantity, line.UnitPrice));
        Assert.Equal(note.Items.Single(i => i.ItemCode == "TRIGO").IcmsOperationValue, line.IcmsOperationValue);
        Assert.Equal(0m, saved.TotalFunrural);
    }

    [Fact]
    public async Task Line_patch_is_restored()
    {
        var (scenario, _, note) = await SeedAsync();
        var line = note.Items.Single(i => i.ItemCode == "TRIGO");
        var patch = new PurchaseInvoiceItem
        {
            Key = line.Key, PurchaseInvoiceKey = note.Key, ItemCode = "MILHO", ItemName = line.ItemName, UnitOfMeasureCode = "KG",
            Quantity = 1m, UnitPrice = 9m, UsageCode = line.UsageCode,
        };

        await new PurchaseInvoicesItemsUpdateService(scenario.Db, new FakeItemService(), Apply(scenario))
            .ExecuteAsync(line.Key!.Value, patch, "tester");

        var saved = (await PurchaseInvoicesNfeIssueServiceTests.ReloadAsync(scenario, note.Key)).Items.Single(i => i.Key == line.Key);
        Assert.Equal(("TRIGO", 1000m, 1.5m), (saved.ItemCode, saved.Quantity, saved.UnitPrice));
    }

    [Fact]
    public async Task Lines_cannot_be_added()
    {
        var (scenario, _, note) = await SeedAsync();
        var extra = new PurchaseInvoiceItem
        {
            Key = Guid.NewGuid(), PurchaseInvoiceKey = note.Key, ItemCode = "MILHO", ItemName = "MILHO EM GRAOS",
            UnitOfMeasureCode = "KG", Quantity = 1m, UnitPrice = 1m,
        };

        var e = await Assert.ThrowsAsync<DefaultException>(() =>
            new PurchaseInvoicesItemsCreateService(scenario.Db, new FakeItemService(), Apply(scenario)).ExecuteAsync(extra, "tester"));

        Assert.Equal(LinesMessage, e.Message);
    }

    [Fact]
    public async Task Lines_cannot_be_removed()
    {
        var (scenario, _, note) = await SeedAsync();

        var e = await Assert.ThrowsAsync<DefaultException>(() =>
            new PurchaseInvoicesItemsDeleteService(scenario.Db, TaxTestServices.PurchaseApply(scenario.Db, new FakeBusinessPartnerService()))
                .ExecuteAsync(note.Items.First().Key!.Value));

        Assert.Equal(LinesMessage, e.Message);
    }

    [Fact]
    public async Task Producer_with_counter_note_cannot_be_reversed()
    {
        var (scenario, producer, note) = await SeedAsync();

        var e = await Assert.ThrowsAsync<DefaultException>(() =>
            new PurchaseInvoicesReverseConfirmService(scenario.Db).ExecuteAsync(producer.Key, "tester"));

        Assert.Equal($"A escrituração tem a contra-nota {note.InvoiceNumber}: cancele ou exclua a contra-nota antes de estornar a confirmação.", e.Message);
    }

    [Fact]
    public async Task Producer_with_counter_note_cannot_be_cancelled()
    {
        var (scenario, producer, note) = await SeedAsync();

        var e = await Assert.ThrowsAsync<DefaultException>(() =>
            new PurchaseInvoicesCancelService(scenario.Db).ExecuteAsync(producer.Key, "tester"));

        Assert.Equal($"A escrituração tem a contra-nota {note.InvoiceNumber}: cancele ou exclua a contra-nota antes de cancelar a escrituração.", e.Message);
    }

    [Fact]
    public async Task Deleting_the_counter_note_frees_the_producer()
    {
        var (scenario, producer, note) = await SeedAsync();

        await new PurchaseInvoicesDeleteService(scenario.Db).ExecuteAsync(note.Key);
        scenario.Db.Context.ChangeTracker.Clear();
        await new PurchaseInvoicesReverseConfirmService(scenario.Db).ExecuteAsync(producer.Key, "tester");

        Assert.Equal(InvoiceStatus.Pending, (await PurchaseInvoicesNfeIssueServiceTests.ReloadAsync(scenario, producer.Key)).InvoiceStatus);
    }

    [Fact]
    public async Task Counter_note_is_not_returnable()
    {
        var (scenario, _, note) = await SeedAsync();
        var stored = await scenario.Db.Context.PurchaseInvoices.SingleAsync(i => i.Key == note.Key);
        stored.InvoiceStatus = InvoiceStatus.Confirmed;
        stored.NfeStatus = NfeStatus.Authorized;
        stored.ChaveNFe = new string('3', 44);
        await scenario.Db.SaveChangesAsync();
        scenario.Db.Context.ChangeTracker.Clear();
        var request = new PurchaseInvoiceNfeReturnRequest(
            note.Key, [new PurchaseInvoiceNfeReturnItem(note.Items.First().Key!.Value, 1m)], "grão fora do padrão");

        var e = await Assert.ThrowsAsync<DefaultException>(() =>
            PurchaseInvoicesThirdPartyReturnTests.Returns(scenario, PurchaseNfeTestSeed.Partners()).ExecuteAsync(request, "tester"));

        Assert.Equal("A contra-nota não é devolvida: a devolução parte da escrituração da NF-e do produtor.", e.Message);
    }

    [Fact]
    public async Task Producer_with_counter_note_is_still_returnable()
    {
        var (scenario, producer, _) = await SeedAsync();
        var request = new PurchaseInvoiceNfeReturnRequest(
            producer.Key, [new PurchaseInvoiceNfeReturnItem(producer.Items.Single(i => i.ItemCode == "MILHO").Key!.Value, 100m)],
            "grão fora do padrão");

        var created = await PurchaseInvoicesThirdPartyReturnTests.Returns(scenario, PurchaseNfeTestSeed.Partners())
            .ExecuteAsync(request, "tester");

        Assert.Equal(producer.Key, created.PurchaseInvoiceOriginKey);
    }
    private const string ProducerReferencedMessage =
        "A escrituração tem contra-nota emitida a partir dela: cancele o documento em vez de excluí-lo.";

    /// <summary>
    /// D11: a contra-nota rejeitada é cancelada e a escrituração pode voltar a Pendente — mas a contra-nota cancelada é
    /// registro fiscal e continua apontando para ela (FKs Restrict: no banco real o delete estouraria 547).
    /// </summary>
    private static async Task<(PurchaseNfeScenario Scenario, PurchaseInvoice Producer, PurchaseInvoice Note)> SeedWithCancelledNoteAsync()
    {
        var (scenario, producer, note) = await SeedAsync();
        var stored = await scenario.Db.Context.PurchaseInvoices.SingleAsync(i => i.Key == note.Key);
        stored.InvoiceStatus = InvoiceStatus.Cancelled;
        await scenario.Db.SaveChangesAsync();
        scenario.Db.Context.ChangeTracker.Clear();

        await new PurchaseInvoicesReverseConfirmService(scenario.Db).ExecuteAsync(producer.Key, "tester");
        scenario.Db.Context.ChangeTracker.Clear();

        return (scenario, await PurchaseInvoicesNfeIssueServiceTests.ReloadAsync(scenario, producer.Key), note);
    }

    [Fact]
    public async Task Producer_referenced_by_a_cancelled_counter_note_cannot_be_deleted()
    {
        var (scenario, producer, _) = await SeedWithCancelledNoteAsync();

        var e = await Assert.ThrowsAsync<DefaultException>(() =>
            new PurchaseInvoicesDeleteService(scenario.Db).ExecuteAsync(producer.Key));

        Assert.Equal(ProducerReferencedMessage, e.Message);
    }

    [Fact]
    public async Task Producer_line_referenced_by_a_cancelled_counter_note_cannot_be_deleted()
    {
        var (scenario, producer, _) = await SeedWithCancelledNoteAsync();
        var line = producer.Items.Single(i => i.ItemCode == "TRIGO");

        var e = await Assert.ThrowsAsync<DefaultException>(() =>
            new PurchaseInvoicesItemsDeleteService(scenario.Db, TaxTestServices.PurchaseApply(scenario.Db, new FakeBusinessPartnerService()))
                .ExecuteAsync(line.Key!.Value));

        Assert.Equal("O item TRIGO foi reproduzido numa contra-nota e não pode ser excluído.", e.Message);
    }

    [Fact]
    public async Task Producer_line_referenced_by_a_cancelled_counter_note_cannot_be_removed_by_the_header_patch()
    {
        var (scenario, producer, _) = await SeedWithCancelledNoteAsync();
        var incoming = await PurchaseInvoicesNfeIssueServiceTests.ReloadAsync(scenario, producer.Key);
        incoming.Items.Remove(incoming.Items.Single(i => i.ItemCode == "TRIGO"));
        scenario.Db.Context.ChangeTracker.Clear();

        var e = await Assert.ThrowsAsync<DefaultException>(() =>
            new PurchaseInvoicesUpdateService(scenario.Db, PurchaseNfeTestSeed.Partners(), new FakeItemService(), Apply(scenario))
                .ExecuteAsync(producer.Key, incoming, "tester"));

        Assert.Equal("O item TRIGO foi reproduzido numa contra-nota e não pode ser excluído.", e.Message);
    }

    [Fact]
    public async Task Header_patch_cannot_add_and_remove_counter_note_lines()
    {
        var (scenario, _, note) = await SeedAsync();
        var incoming = await PurchaseInvoicesNfeIssueServiceTests.ReloadAsync(scenario, note.Key);
        incoming.Items.Remove(incoming.Items.Single(i => i.ItemCode == "TRIGO"));
        incoming.Items.Add(new PurchaseInvoiceItem
        {
            PurchaseInvoiceKey = note.Key, ItemCode = "MILHO", ItemName = "MILHO EM GRAOS", UnitOfMeasureCode = "KG",
            Quantity = 1m, UnitPrice = 1m,
        });
        scenario.Db.Context.ChangeTracker.Clear();

        var e = await Assert.ThrowsAsync<DefaultException>(() =>
            new PurchaseInvoicesUpdateService(scenario.Db, PurchaseNfeTestSeed.Partners(), new FakeItemService(), Apply(scenario))
                .ExecuteAsync(note.Key, incoming, "tester"));

        Assert.Equal(LinesMessage, e.Message);
    }
}
