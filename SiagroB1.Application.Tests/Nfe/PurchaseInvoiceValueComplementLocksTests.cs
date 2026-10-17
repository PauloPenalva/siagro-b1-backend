using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Nfe;
using SiagroB1.Application.Services.PurchaseInvoices;
using SiagroB1.Application.Services.Taxes;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using static SiagroB1.Application.Tests.Support.NfeCancelTestServices;
using static SiagroB1.Application.Tests.Support.ValueComplementNfeSeed;

namespace SiagroB1.Application.Tests.Nfe;

/// <summary>Spec 2026-10-14, D4 e regras 13–14: a original não é cancelada com complemento ativo; o complemento não é devolvido.</summary>
public class PurchaseInvoiceValueComplementLocksTests
{
    private const string Reason = "Entrada lancada em duplicidade";
    private const string Locked = "A nota DE000005 tem o complemento ST-0001 ativo: cancele o complemento antes.";

    private static async Task<PurchaseInvoice> ComplementAsync(ValueComplementScenario s)
    {
        var invoice = s.Complement((s.OriginalItemKey, 300m));
        await Create(s.Db).ExecuteAsync(invoice, "ana");
        return invoice;
    }

    private static PurchaseInvoicesNfeReturnCreateService Returns(ValueComplementScenario s) =>
        new(s.Db, new TaxCalculationGate(s.Db, NfeTestSeed.Config()), Create(s.Db), NfeTestSeed.Clock,
            NfeIssueInputAssembler.BrasiliaZone);

    [Fact]
    public async Task Nfe_cancel_of_an_original_with_an_active_complement_is_refused_before_sefaz()
    {
        var s = await SeedAsync();
        await ComplementAsync(s);
        var sefaz = new FakeNfeSefazClient();

        var e = await Assert.ThrowsAsync<DefaultException>(() =>
            PurchaseCancel(s.Db, sefaz).ExecuteAsync(s.OriginalKey, Reason, "tester"));

        Assert.Equal(Locked, e.Message);
        Assert.Empty(sefaz.CancelRequests);
    }

    [Fact]
    public async Task Document_cancel_rehearsal_of_the_original_is_refused()
    {
        var s = await SeedAsync();
        await ComplementAsync(s);

        var e = await Assert.ThrowsAsync<DefaultException>(() =>
            FinancialDocumentTestServices.PurchaseCancel(s.Db).EnsureCanCancelAsync(s.OriginalKey));

        Assert.Equal(Locked, e.Message);
    }

    [Fact]
    public async Task Complement_lock_comes_before_the_return_lock()
    {
        // O caso da nº 5 da CEAGUI dev: a devolução DE000002 (Rejeitada, Pendente) ainda aponta a original.
        var s = await SeedAsync();
        await ComplementAsync(s);
        s.Db.Context.PurchaseInvoices.Add(new PurchaseInvoice
        {
            Key = Guid.NewGuid(), BranchCode = "01", CardCode = PurchaseNfeTestSeed.Supplier, IssuerType = DocumentIssuerType.Own,
            InvoiceType = PurchaseInvoiceType.Return, IsNfeReturn = true, InvoiceStatus = InvoiceStatus.Pending,
            NfeStatus = NfeStatus.Rejected, PurchaseInvoiceOriginKey = s.OriginalKey,
        });
        await s.Db.SaveChangesAsync();

        var e = await Assert.ThrowsAsync<DefaultException>(() =>
            FinancialDocumentTestServices.PurchaseCancel(s.Db).EnsureCanCancelAsync(s.OriginalKey));

        Assert.Equal(Locked, e.Message);
    }

    [Fact]
    public async Task Cancelled_complement_releases_the_original()
    {
        var s = await SeedAsync();
        var complement = await ComplementAsync(s);
        await ChangeAsync(s.Db, complement.Key, c => c.InvoiceStatus = InvoiceStatus.Cancelled);
        var sefaz = new FakeNfeSefazClient();
        sefaz.CancelResponses.Enqueue(r => FakeNfeSefazClient.CancellationRegistered(r.AccessKey));

        var outcome = await PurchaseCancel(s.Db, sefaz).ExecuteAsync(s.OriginalKey, Reason, "tester");

        Assert.Equal(NfeStatus.Cancelled, outcome.NfeStatus);
        Assert.Equal(InvoiceStatus.Cancelled, outcome.InvoiceStatus);
    }

    [Fact]
    public async Task Complement_has_no_return()
    {
        var s = await SeedAsync();
        var complement = await ComplementAsync(s);

        var e = await Assert.ThrowsAsync<DefaultException>(() => Returns(s).ExecuteAsync(
            new PurchaseInvoiceNfeReturnRequest(
                complement.Key, [new PurchaseInvoiceNfeReturnItem(complement.Items.Single().Key!.Value, 1m)], "grão fora do padrão"),
            "tester"));

        Assert.Equal("Complemento de valor não tem devolução.", e.Message);
    }

    [Fact]
    public async Task Original_is_still_returned_alone()
    {
        var s = await SeedAsync();
        await ComplementAsync(s);

        var returned = await Returns(s).ExecuteAsync(
            new PurchaseInvoiceNfeReturnRequest(
                s.OriginalKey, [new PurchaseInvoiceNfeReturnItem(s.OriginalItemKey, 400m)], "grão fora do padrão"),
            "tester");

        var line = Assert.Single(returned.Items);
        Assert.Equal(s.OriginalItemKey, line.PurchaseInvoiceItemOriginKey);
        Assert.Equal(400m, line.Quantity);
        Assert.Null(line.ComplementedItemKey);
    }
}
