using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using static SiagroB1.Application.Tests.Support.NfeCancelTestServices;
using static SiagroB1.Application.Tests.Support.NfeCorrectionTestServices;

namespace SiagroB1.Application.Tests.Nfe;

/// <summary>CC-e da entrada própria (spec 2026-10-06 §7.1, T6).</summary>
public class PurchaseInvoicesNfeCorrectionServiceTests
{
    private const string Text = "Onde se le placa ABC1D23, leia-se placa XYZ9K87.";

    [Fact]
    public async Task Own_entry_receives_a_correction()
    {
        var scenario = await PurchaseNfeTestSeed.SeedAsync();
        await AuthorizePurchaseAsync(scenario.Db, scenario.InvoiceKey);
        var sefaz = new FakeNfeSefazClient();
        sefaz.CorrectionResponses.Enqueue(r => FakeNfeSefazClient.CorrectionRegistered(r.Sequence, r.Text));

        var outcome = await PurchaseCorrection(scenario.Db, sefaz).ExecuteAsync(scenario.InvoiceKey, Text, "tester");

        Assert.Equal(1, outcome.Sequence);
        Assert.True(await scenario.Db.Context.PurchaseInvoiceNfeCorrections.AnyAsync(x =>
            x.PurchaseInvoiceKey == scenario.InvoiceKey && x.Sequence == 1));
    }

    [Fact]
    public async Task Third_party_entry_is_refused_without_calling_sefaz()
    {
        var scenario = await PurchaseNfeTestSeed.SeedAsync();
        await AuthorizePurchaseAsync(scenario.Db, scenario.InvoiceKey);
        (await scenario.Db.Context.PurchaseInvoices.SingleAsync(x => x.Key == scenario.InvoiceKey)).IssuerType = DocumentIssuerType.ThirdParty;
        await scenario.Db.SaveChangesAsync();
        var sefaz = new FakeNfeSefazClient();

        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            PurchaseCorrection(scenario.Db, sefaz).ExecuteAsync(scenario.InvoiceKey, Text, "tester"));

        Assert.Equal("NF-e de terceiro não recebe carta de correção pelo Siagro.", ex.Message);
        Assert.Empty(sefaz.CorrectionRequests);
    }
}
