using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Tests.PurchaseInvoices;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Application.Tests.Nfe;

/// <summary>Emissão da contra-nota (spec 2026-10-10 §6): NFref com a chave do produtor e o Funrural da escrituração no infCpl.</summary>
public class PurchaseInvoicesCounterNoteIssueTests
{
    [Fact]
    public async Task Counter_note_references_the_producer_key_and_carries_the_producer_funrural()
    {
        var (scenario, producer) = await CounterNoteSeed.SeedAsync();
        var note = await PurchaseInvoicesCounterNoteTests.Service(scenario).ExecuteAsync(producer.Key, "tester");
        scenario.Db.Context.ChangeTracker.Clear();
        var sefaz = new FakeNfeSefazClient();
        sefaz.AuthorizeResponses.Enqueue(key => FakeNfeSefazClient.Authorized(key));

        var outcome = await PurchaseInvoicesNfeIssueServiceTests.Issue(scenario, sefaz).ExecuteAsync(note.Key, "tester");

        Assert.Equal(NfeStatus.Authorized, outcome.NfeStatus);
        var signed = Assert.Single(sefaz.Sent).Xml;
        Assert.Contains("<tpNF>0</tpNF>", signed);
        Assert.Contains("<finNFe>1</finNFe>", signed);
        Assert.Contains($"<NFref><refNFe>{SupplierNfeXml.AccessKey}</refNFe></NFref>", signed);
        Assert.Contains("Contribuição Social denominada Funrural", signed);

        var saved = await PurchaseInvoicesNfeIssueServiceTests.ReloadAsync(scenario, note.Key);
        Assert.Equal(InvoiceStatus.Confirmed, saved.InvoiceStatus);
        Assert.Equal(0m, saved.TotalFunrural);
    }

    [Fact]
    public async Task Producer_no_longer_confirmed_blocks_the_issue_before_the_number()
    {
        var (scenario, producer) = await CounterNoteSeed.SeedAsync();
        var note = await PurchaseInvoicesCounterNoteTests.Service(scenario).ExecuteAsync(producer.Key, "tester");
        var context = TestDb.CreateUnitOfWork(scenario.DatabaseName).Context;
        (await context.PurchaseInvoices.SingleAsync(i => i.Key == producer.Key)).InvoiceStatus = InvoiceStatus.Pending;
        await context.SaveChangesAsync();
        scenario.Db.Context.ChangeTracker.Clear();
        var reservation = new FakeNfeNumberReservationService(10);

        var e = await Assert.ThrowsAsync<DefaultException>(() =>
            PurchaseInvoicesNfeIssueServiceTests.Issue(scenario, new FakeNfeSefazClient(), reservation).ExecuteAsync(note.Key, "tester"));

        Assert.Equal("A escrituração da NF-e do produtor precisa estar confirmada e com a chave de 44 dígitos para emitir a contra-nota.", e.Message);
        Assert.Equal(0, reservation.Calls);
    }
}
