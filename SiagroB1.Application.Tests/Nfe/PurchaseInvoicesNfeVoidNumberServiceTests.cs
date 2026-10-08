using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Nfe;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using static SiagroB1.Application.Tests.Support.NfeVoidNumberTestServices;

namespace SiagroB1.Application.Tests.Nfe;

public class PurchaseInvoicesNfeVoidNumberServiceTests
{
    private const string Reason = "Entrada propria rejeitada e cancelada";

    [Fact]
    public async Task Own_entry_with_rejected_nfe_is_voided()
    {
        var scenario = await PurchaseNfeTestSeed.SeedAsync();
        await RejectAndCancelPurchaseAsync(scenario.Db, scenario.InvoiceKey);
        var sefaz = new FakeNfeSefazClient();
        sefaz.VoidNumberResponses.Enqueue(FakeNfeSefazClient.NumberVoided);

        var outcome = await PurchaseVoid(scenario.Db, sefaz).ExecuteAsync(scenario.InvoiceKey, Reason, "tester");

        Assert.Equal(NfeStatus.Voided, outcome.NfeStatus);
        var saved = await scenario.Db.Context.PurchaseInvoices.AsNoTracking().SingleAsync(x => x.Key == scenario.InvoiceKey);
        Assert.Equal(FakeNfeSefazClient.VoidNumberProtocol, saved.NfeProtocol);
        Assert.True(await scenario.Db.Context.PurchaseInvoiceNfeXmls.AnyAsync(x =>
            x.PurchaseInvoiceKey == scenario.InvoiceKey && x.Kind == NfeXmlKind.NumberVoid));
        Assert.Equal(233, Assert.Single(sefaz.VoidNumberRequests).Number);
    }

    [Fact]
    public async Task Third_party_entry_is_not_voided()
    {
        var scenario = await PurchaseNfeTestSeed.SeedAsync();
        await RejectAndCancelPurchaseAsync(scenario.Db, scenario.InvoiceKey);
        (await scenario.Db.Context.PurchaseInvoices.SingleAsync(x => x.Key == scenario.InvoiceKey)).IssuerType = DocumentIssuerType.ThirdParty;
        await scenario.Db.SaveChangesAsync();
        var sefaz = new FakeNfeSefazClient();

        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            PurchaseVoid(scenario.Db, sefaz).ExecuteAsync(scenario.InvoiceKey, Reason, "tester"));

        Assert.Equal(PurchaseInvoicesNfeVoidNumberService.ThirdPartyMessage, ex.Message);
        Assert.Empty(sefaz.VoidNumberRequests);
    }
}
