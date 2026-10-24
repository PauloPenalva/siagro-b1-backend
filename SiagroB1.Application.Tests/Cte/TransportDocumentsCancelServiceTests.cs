using SiagroB1.Application.Services.Cte;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Application.Tests.Cte;

public class TransportDocumentsCancelServiceTests : CteEventsTestBase
{
    private const string Reason = "Cancelamento por erro de digitacao";

    private sealed class RecordingHook : ICteCancellationHook
    {
        public List<TransportDocument> Cancelled { get; } = [];

        public Task OnCancelledAsync(TransportDocument doc)
        {
            Cancelled.Add(doc);
            return Task.CompletedTask;
        }
    }

    private readonly RecordingHook _hook = new();

    private TransportDocumentsCancelService Service() =>
        new(Db, NfeTestSeed.Config(), Numbers, Contexts(), Sefaz, _hook, FinancialDocumentTestServices.Transport(Db), () => Clock);

    [Fact]
    public async Task Cancel_short_justification_is_rejected()
    {
        var key = await SeedAsync();

        var e = await Assert.ThrowsAsync<DefaultException>(() => Service().CancelAsync(key, "curta", "tester"));

        Assert.Equal("A justificativa do cancelamento tem de 15 a 255 caracteres.", e.Message);
        Assert.Equal(0, Sefaz.CancelCalls);
    }

    [Fact]
    public async Task Cancel_after_168h_is_rejected()
    {
        var key = await SeedAsync(authorizedAt: Now.DateTime.AddHours(-169));

        var e = await Assert.ThrowsAsync<DefaultException>(() => Service().CancelAsync(key, Reason, "tester"));

        Assert.Equal("O prazo de cancelamento do CT-e (168 horas) acabou.", e.Message);
        Assert.Equal(0, Sefaz.CancelCalls);
    }

    [Fact]
    public async Task Cancel_requires_authorized_status()
    {
        var key = await SeedAsync(CteStatus.Confirmed);

        await Assert.ThrowsAsync<DefaultException>(() => Service().CancelAsync(key, Reason, "tester"));

        Assert.Equal(0, Sefaz.CancelCalls);
    }

    [Fact]
    public async Task Cancel_135_marks_cancelled_and_calls_hook()
    {
        var key = await SeedAsync();
        Sefaz.CancelResponses.Enqueue(r => FakeCteSefazClient.CancellationRegistered(r));

        var result = await Service().CancelAsync(key, Reason, "tester");

        Assert.Equal(135, result.StatusCode);
        var doc = await LoadAsync(key);
        Assert.Equal(CteStatus.Cancelled, doc.Status);
        Assert.Equal(FakeCteSefazClient.EventProtocol, doc.CancellationProtocol);
        Assert.Equal(Reason, doc.CancellationReason);
        Assert.NotNull(doc.CancelledAt);
        Assert.Contains("procEventoCTe", doc.Xmls.Single(x => x.Kind == CteXmlKind.Cancellation).Xml);
        Assert.Equal(FakeCteSefazClient.AuthorizationProtocol, Sefaz.CancelRequests.Single().Protocol);
        Assert.Single(_hook.Cancelled);
        Assert.Equal(0, Numbers.LocksHeld);
    }

    [Fact]
    public async Task Cancel_refused_by_sefaz_keeps_authorized_and_skips_hook()
    {
        var key = await SeedAsync();
        Sefaz.CancelResponses.Enqueue(_ => new SiagroB1.Fiscal.Nfe.NfeEventResult(218, "Rejeição: CT-e já está cancelado"));

        var result = await Service().CancelAsync(key, Reason, "tester");

        Assert.Equal(218, result.StatusCode);
        Assert.Equal(CteStatus.Authorized, (await LoadAsync(key)).Status);
        Assert.Empty(_hook.Cancelled);
    }

    [Fact]
    public async Task Cancel_is_refused_while_emission_is_busy()
    {
        var key = await SeedAsync();
        Numbers.EmissionBusy = true;

        await Assert.ThrowsAsync<DefaultException>(() => Service().CancelAsync(key, Reason, "tester"));

        Assert.Equal(0, Sefaz.CancelCalls);
    }

    private async Task AddInvoiceAsync(Guid cteKey, FreightInvoiceStatus status, bool itemActive)
    {
        var invoice = new FreightInvoice
        {
            BranchCode = "01", Code = "FF-77", TakerCardCode = "TOM", CreatedBy = "t", Status = status,
            Items = [new FreightInvoiceItem { TransportDocumentKey = cteKey, Amount = 10m, IsActive = itemActive }],
        };
        Db.Context.FreightInvoices.Add(invoice);
        await Db.SaveChangesAsync();
    }

    [Fact]
    public async Task Cancel_CteInActiveInvoice_RejectedWithInvoiceCode()
    {
        var key = await SeedAsync();
        await AddInvoiceAsync(key, FreightInvoiceStatus.Issued, true);

        var e = await Assert.ThrowsAsync<DefaultException>(() => Service().CancelAsync(key, Reason, "tester"));

        Assert.Equal("CT-e está na fatura FF-77; retire-o ou cancele a fatura antes.", e.Message);
        Assert.Equal(0, Sefaz.CancelCalls);
    }

    [Fact]
    public async Task Cancel_CteInCancelledInvoice_Allowed()
    {
        var key = await SeedAsync();
        await AddInvoiceAsync(key, FreightInvoiceStatus.Cancelled, false);
        Sefaz.CancelResponses.Enqueue(r => FakeCteSefazClient.CancellationRegistered(r));

        var result = await Service().CancelAsync(key, Reason, "tester");

        Assert.Equal(135, result.StatusCode);
        Assert.Equal(CteStatus.Cancelled, (await LoadAsync(key)).Status);
    }
}
