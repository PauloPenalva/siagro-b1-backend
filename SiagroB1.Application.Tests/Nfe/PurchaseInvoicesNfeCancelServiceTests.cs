using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.PurchaseInvoices;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;
using static SiagroB1.Application.Tests.Support.NfeCancelTestServices;

namespace SiagroB1.Application.Tests.Nfe;

public class PurchaseInvoicesNfeCancelServiceTests
{
    private const string Reason = "Entrada lancada em duplicidade";

    /// <summary>Cancelamento local que estoura depois de mexer no documento (fase 2 falhando).</summary>
    private sealed class FailingCancel(UnitOfWork db) : PurchaseInvoicesCancelService(db)
    {
        public override async Task CancelAfterNfeAsync(Guid key, string userName)
        {
            (await db.Context.PurchaseInvoices.SingleAsync(x => x.Key == key)).InvoiceStatus = InvoiceStatus.Cancelled;
            throw new ApplicationException("Estoque travado por outro usuário.");
        }
    }

    [Fact]
    public async Task Own_entry_with_authorized_nfe_is_cancelled()
    {
        var scenario = await PurchaseNfeTestSeed.SeedAsync();
        await AuthorizePurchaseAsync(scenario.Db, scenario.InvoiceKey);
        var sefaz = new FakeNfeSefazClient();
        sefaz.CancelResponses.Enqueue(r => FakeNfeSefazClient.CancellationRegistered(r.AccessKey));

        var outcome = await PurchaseCancel(scenario.Db, sefaz).ExecuteAsync(scenario.InvoiceKey, Reason, "tester");

        Assert.Equal(NfeStatus.Cancelled, outcome.NfeStatus);
        Assert.Equal(InvoiceStatus.Cancelled, outcome.InvoiceStatus);
        var saved = await scenario.Db.Context.PurchaseInvoices.AsNoTracking().SingleAsync(x => x.Key == scenario.InvoiceKey);
        Assert.Equal(Reason, saved.NfeCancellationReason);
        Assert.Equal("tester", saved.CanceledBy);
        Assert.True(await scenario.Db.Context.PurchaseInvoiceNfeXmls.AnyAsync(x =>
            x.PurchaseInvoiceKey == scenario.InvoiceKey && x.Kind == NfeXmlKind.CancellationEvent));
    }

    [Fact]
    public async Task Third_party_entry_is_not_cancelled_at_sefaz()
    {
        var scenario = await PurchaseNfeTestSeed.SeedAsync();
        await AuthorizePurchaseAsync(scenario.Db, scenario.InvoiceKey);
        (await scenario.Db.Context.PurchaseInvoices.SingleAsync(x => x.Key == scenario.InvoiceKey)).IssuerType = DocumentIssuerType.ThirdParty;
        await scenario.Db.SaveChangesAsync();
        var sefaz = new FakeNfeSefazClient();

        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            PurchaseCancel(scenario.Db, sefaz).ExecuteAsync(scenario.InvoiceKey, Reason, "tester"));

        Assert.Equal("NF-e de terceiro não é cancelada pelo Siagro: use o Cancelar do documento.", ex.Message);
        Assert.Empty(sefaz.CancelRequests);
    }

    [Fact]
    public async Task Entry_with_an_active_return_is_refused_before_calling_sefaz()
    {
        var scenario = await PurchaseNfeTestSeed.SeedAsync();
        await AuthorizePurchaseAsync(scenario.Db, scenario.InvoiceKey);
        var origin = await scenario.Db.Context.PurchaseInvoices.SingleAsync(x => x.Key == scenario.InvoiceKey);
        scenario.Db.Context.PurchaseInvoices.Add(new Domain.Entities.PurchaseInvoice
        {
            Key = Guid.NewGuid(), BranchCode = origin.BranchCode, CardCode = origin.CardCode, InvoiceType = PurchaseInvoiceType.Return,
            IssuerType = DocumentIssuerType.Own, IsNfeReturn = true, InvoiceStatus = InvoiceStatus.Pending,
            PurchaseInvoiceOriginKey = origin.Key,
        });
        await scenario.Db.SaveChangesAsync();
        var sefaz = new FakeNfeSefazClient();

        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            PurchaseCancel(scenario.Db, sefaz).ExecuteAsync(scenario.InvoiceKey, Reason, "tester"));

        Assert.Equal("Documento de entrada possui devolução.", ex.Message);
        Assert.Empty(sefaz.CancelRequests);
    }

    [Fact]
    public async Task Local_failure_after_sefaz_keeps_the_entry_nfe_cancelled_and_complete_finishes_it()
    {
        var scenario = await PurchaseNfeTestSeed.SeedAsync();
        await AuthorizePurchaseAsync(scenario.Db, scenario.InvoiceKey);
        var sefaz = new FakeNfeSefazClient();
        sefaz.CancelResponses.Enqueue(r => FakeNfeSefazClient.CancellationRegistered(r.AccessKey));

        var failed = await PurchaseCancel(scenario.Db, sefaz, new FailingCancel(scenario.Db))
            .ExecuteAsync(scenario.InvoiceKey, Reason, "tester");

        Assert.Equal(NfeStatus.Cancelled, failed.NfeStatus);
        Assert.Equal("Estoque travado por outro usuário.", failed.CancellationError);
        var saved = await scenario.Db.Context.PurchaseInvoices.AsNoTracking().SingleAsync(x => x.Key == scenario.InvoiceKey);
        Assert.Equal(NfeStatus.Cancelled, saved.NfeStatus);
        Assert.Equal(InvoiceStatus.Confirmed, saved.InvoiceStatus);
        Assert.Equal("Estoque travado por outro usuário.", saved.NfeCancellationError);
        scenario.Db.Context.ChangeTracker.Clear();

        var outcome = await PurchaseComplete(scenario.Db).ExecuteAsync(scenario.InvoiceKey, "tester");

        Assert.Equal(InvoiceStatus.Cancelled, outcome.InvoiceStatus);
        Assert.Null(outcome.CancellationError);
        Assert.Single(sefaz.CancelRequests);
        var completed = await scenario.Db.Context.PurchaseInvoices.AsNoTracking().SingleAsync(x => x.Key == scenario.InvoiceKey);
        Assert.Equal(InvoiceStatus.Cancelled, completed.InvoiceStatus);
        Assert.Null(completed.NfeCancellationError);
    }
}
