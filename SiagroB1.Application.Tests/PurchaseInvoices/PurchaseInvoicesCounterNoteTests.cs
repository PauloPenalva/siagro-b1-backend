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

/// <summary>"Gerar contra-nota" (spec 2026-10-10 §5): cópia da escrituração, sem recálculo e sem Funrural, e as recusas.</summary>
public class PurchaseInvoicesCounterNoteTests
{
    internal static PurchaseInvoicesCreateService Create(PurchaseNfeScenario scenario, string? erp = "STANDALONE") =>
        new(scenario.Db, PurchaseNfeTestSeed.Partners(), new FakeItemService(),
            TaxTestServices.PurchaseApply(scenario.Db, PurchaseNfeTestSeed.Partners(), erp), new FakeDocNumberSequenceService());

    internal static PurchaseInvoicesCounterNoteCreateService Service(PurchaseNfeScenario scenario, string? erp = "STANDALONE") =>
        new(scenario.Db, new TaxCalculationGate(scenario.Db, NfeTestSeed.Config(erp)), Create(scenario, erp),
            NfeTestSeed.Clock, NfeIssueInputAssembler.BrasiliaZone);

    private const string OriginRule =
        "A contra-nota parte da escrituração de uma NF-e de produtor: documento de terceiro, Normal, confirmado e com a chave de 44 dígitos.";

    [Fact]
    public async Task Creates_a_pending_own_entry_with_the_producer_lines_and_taxes()
    {
        var (scenario, producer) = await CounterNoteSeed.SeedAsync();

        var created = await Service(scenario).ExecuteAsync(producer.Key, "tester");

        var note = await PurchaseInvoicesNfeIssueServiceTests.ReloadAsync(scenario, created.Key);
        Assert.Equal((DocumentIssuerType.Own, PurchaseInvoiceType.Normal, InvoiceStatus.Pending, TaxDocumentKind.Nfe),
            (note.IssuerType, note.InvoiceType, note.InvoiceStatus, note.TaxDocumentKind));
        Assert.Equal(producer.Key, note.ProducerInvoiceKey);
        Assert.Equal((producer.CardCode, producer.PaymentConditionCode, producer.GrossWeight, producer.NetWeight),
            (note.CardCode, note.PaymentConditionCode, note.GrossWeight, note.NetWeight));
        Assert.False(note.IsNfeReturn);
        Assert.Null(note.ChaveNFe);
        Assert.Equal(2, note.Items.Count);

        foreach (var line in note.Items)
        {
            var source = producer.Items.Single(i => i.Key == line.ProducerInvoiceItemKey);
            Assert.Equal((source.ItemCode, source.Quantity, source.UnitPrice, source.UsageCode, source.Cfop),
                (line.ItemCode, line.Quantity, line.UnitPrice, line.UsageCode, line.Cfop));
            Assert.Equal((source.CstIcms, source.IcmsBase, source.IcmsOperationValue, source.IcmsDeferredValue, source.IcmsBenefitCode),
                (line.CstIcms, line.IcmsBase, line.IcmsOperationValue, line.IcmsDeferredValue, line.IcmsBenefitCode));
            Assert.Equal(0m, line.FunruralValue);
            Assert.Null(line.NfeItemNumber);
        }
    }

    [Fact]
    public async Task Producer_is_not_changed()
    {
        var (scenario, producer) = await CounterNoteSeed.SeedAsync();

        await Service(scenario).ExecuteAsync(producer.Key, "tester");

        var reloaded = await PurchaseInvoicesNfeIssueServiceTests.ReloadAsync(scenario, producer.Key);
        Assert.Equal(InvoiceStatus.Confirmed, reloaded.InvoiceStatus);
        Assert.Null(reloaded.ProducerInvoiceKey);
        Assert.Equal(3m, reloaded.TotalFunrural);
    }

    [Fact]
    public async Task Body_cannot_link_an_entry_to_a_producer()
    {
        var (scenario, producer) = await CounterNoteSeed.SeedAsync();
        var invoice = new PurchaseInvoice
        {
            Key = Guid.NewGuid(), BranchCode = "01", CardCode = PurchaseNfeTestSeed.Supplier, IssuerType = DocumentIssuerType.Own,
            InvoiceType = PurchaseInvoiceType.Normal, IssueDate = new DateTime(2026, 10, 2), ProducerInvoiceKey = producer.Key,
        };
        invoice.AddItem(new PurchaseInvoiceItem
        {
            Key = Guid.NewGuid(), ItemCode = "TRIGO", ItemName = "TRIGO EM GRAOS", UnitOfMeasureCode = "KG", Quantity = 1m,
            UnitPrice = 1m, UsageCode = producer.Items.First().UsageCode, ProducerInvoiceItemKey = producer.Items.First().Key,
        });

        await Create(scenario).ExecuteAsync(invoice, "tester");

        var saved = await PurchaseInvoicesNfeIssueServiceTests.ReloadAsync(scenario, invoice.Key);
        Assert.Null(saved.ProducerInvoiceKey);
        Assert.Null(saved.Items.Single().ProducerInvoiceItemKey);
    }

    [Theory]
    [InlineData("pending")]
    [InlineData("keyless")]
    [InlineData("other-kind")]
    [InlineData("return")]
    public async Task Producer_outside_the_rule_is_refused(string kind)
    {
        var (scenario, producer) = await CounterNoteSeed.SeedAsync();
        var stored = await scenario.Db.Context.PurchaseInvoices.SingleAsync(i => i.Key == producer.Key);
        switch (kind)
        {
            case "pending": stored.InvoiceStatus = InvoiceStatus.Pending; break;
            case "keyless": stored.ChaveNFe = null; break;
            case "other-kind": stored.TaxDocumentKind = TaxDocumentKind.Other; break;
            case "return": stored.InvoiceType = PurchaseInvoiceType.Return; break;
        }
        await scenario.Db.SaveChangesAsync();
        scenario.Db.Context.ChangeTracker.Clear();

        var e = await Assert.ThrowsAsync<DefaultException>(() => Service(scenario).ExecuteAsync(producer.Key, "tester"));

        Assert.Equal(OriginRule, e.Message);
    }

    [Fact]
    public async Task Own_entry_cannot_originate_a_counter_note()
    {
        var scenario = await PurchaseNfeTestSeed.SeedAsync();

        var e = await Assert.ThrowsAsync<DefaultException>(() => Service(scenario).ExecuteAsync(scenario.InvoiceKey, "tester"));

        Assert.Equal(OriginRule, e.Message);
    }

    [Fact]
    public async Task Usage_without_the_flag_is_refused()
    {
        var (scenario, producer) = await CounterNoteSeed.SeedAsync(allowsCounterNote: false);

        var e = await Assert.ThrowsAsync<DefaultException>(() => Service(scenario).ExecuteAsync(producer.Key, "tester"));

        Assert.Equal("A natureza COMPRA DE MERCADORIA do item TRIGO não permite contra-nota.", e.Message);
    }

    [Fact]
    public async Task Line_without_usage_is_refused()
    {
        var (scenario, producer) = await CounterNoteSeed.SeedAsync();
        (await scenario.Db.Context.PurchaseInvoicesItems.SingleAsync(i => i.PurchaseInvoiceKey == producer.Key && i.ItemCode == "MILHO"))
            .UsageCode = null;
        await scenario.Db.SaveChangesAsync();
        scenario.Db.Context.ChangeTracker.Clear();

        var e = await Assert.ThrowsAsync<DefaultException>(() => Service(scenario).ExecuteAsync(producer.Key, "tester"));

        Assert.Equal("O item MILHO da escrituração está sem natureza de operação.", e.Message);
    }

    [Fact]
    public async Task Branch_without_nfe_is_refused()
    {
        var (scenario, producer) = await CounterNoteSeed.SeedAsync();

        var e = await Assert.ThrowsAsync<DefaultException>(() => Service(scenario, erp: "SAPB1").ExecuteAsync(producer.Key, "tester"));

        Assert.Equal("A filial 01 não emite NF-e pelo Siagro.", e.Message);
    }

    [Fact]
    public async Task Second_counter_note_is_refused()
    {
        var (scenario, producer) = await CounterNoteSeed.SeedAsync();
        var first = await Service(scenario).ExecuteAsync(producer.Key, "tester");

        var e = await Assert.ThrowsAsync<DefaultException>(() => Service(scenario).ExecuteAsync(producer.Key, "tester"));

        Assert.Equal($"A escrituração já tem a contra-nota {first.InvoiceNumber}.", e.Message);
    }

    [Theory]
    [InlineData("document-cancelled")]
    [InlineData("nfe-voided")]
    [InlineData("nfe-denied")]
    public async Task Cancelled_counter_note_does_not_block_a_new_one(string kind)
    {
        var (scenario, producer) = await CounterNoteSeed.SeedAsync();
        var first = await Service(scenario).ExecuteAsync(producer.Key, "tester");
        var stored = await scenario.Db.Context.PurchaseInvoices.SingleAsync(i => i.Key == first.Key);
        switch (kind)
        {
            case "document-cancelled": stored.InvoiceStatus = InvoiceStatus.Cancelled; break;
            case "nfe-voided": stored.NfeStatus = NfeStatus.Voided; break;
            case "nfe-denied": stored.NfeStatus = NfeStatus.Denied; break;
        }
        await scenario.Db.SaveChangesAsync();
        scenario.Db.Context.ChangeTracker.Clear();

        var second = await Service(scenario).ExecuteAsync(producer.Key, "tester");

        Assert.NotEqual(first.Key, second.Key);
    }
}
