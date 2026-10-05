using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.Nfe;
using SiagroB1.Application.Services.Taxes;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Application.Tests.Nfe;

/// <summary>"Emitir NF-e" da devolução própria (spec §8–§9): entrada, finalidade 4, venda referenciada.</summary>
public class SalesInvoicesNfeReturnIssueTests
{
    private static SalesInvoicesNfeIssueService Issue(NfeScenario scenario, FakeNfeSefazClient sefaz, TimeZoneInfo? storageZone = null)
    {
        var config = NfeTestSeed.Config();
        var options = new NfeOptions(config);

        return new SalesInvoicesNfeIssueService(
            scenario.Db, new TaxCalculationGate(scenario.Db, config), new NfeReadinessValidator(scenario.Db, options),
            new BranchNfeSettingsService(scenario.Db, options, sefaz), new FakeNfeNumberReservationService(), sefaz,
            new SalesInvoiceNfeResultHandler(scenario.Db, new RecordingConfirmService(scenario.Db), NullLogger<SalesInvoiceNfeResultHandler>.Instance),
            options, NullLogger<SalesInvoicesNfeIssueService>.Instance, NfeTestSeed.Clock, storageZone ?? NfeIssueInputAssembler.BrasiliaZone);
    }

    private static async Task<string> SignedXmlAsync(NfeScenario scenario, Guid invoiceKey) =>
        (await scenario.Db.Context.SalesInvoiceNfeXmls.AsNoTracking()
            .SingleAsync(x => x.SalesInvoiceKey == invoiceKey && x.Kind == SalesInvoiceNfeXmlKind.Signed)).Xml;

    [Fact]
    public async Task Own_return_is_issued_as_an_incoming_return_referencing_the_sale()
    {
        var s = await NfeReturnTestSeed.SeedAsync();
        var created = await NfeReturnTestSeed.CreateReturnAsync(s, 10000m);
        var sefaz = new FakeNfeSefazClient();
        sefaz.AuthorizeResponses.Enqueue(key => FakeNfeSefazClient.Authorized(key));

        var outcome = await Issue(s.Sale, sefaz).ExecuteAsync(created.Key, "tester");

        Assert.Equal(NfeStatus.Authorized, outcome.NfeStatus);
        var xml = await SignedXmlAsync(s.Sale, created.Key);
        Assert.Contains("<tpNF>0</tpNF>", xml);
        Assert.Contains("<finNFe>4</finNFe>", xml);
        Assert.DoesNotContain("<NFref>", xml);
        Assert.Contains($"<DFeReferenciado><chaveAcesso>{NfeReturnTestSeed.SaleAccessKey}</chaveAcesso><nItem>1</nItem></DFeReferenciado>", xml);
        Assert.Contains("<CFOP>2202</CFOP>", xml);
        Assert.Contains("<tPag>90</tPag>", xml);
        Assert.DoesNotContain("<cobr>", xml);
        Assert.Contains("<natOp>DEVOLUCAO DE VENDA</natOp>", xml);
        Assert.Contains($"Devolução da NF-e nº 1, série 1, de 02/10/2026, chave {NfeReturnTestSeed.SaleAccessKey}.", xml);
    }

    [Fact]
    public async Task Returning_only_the_second_item_references_item_2_of_the_sale()
    {
        var s = await NfeReturnTestSeed.SeedAsync();
        var sale = await s.Sale.Db.Context.SalesInvoices.Include(i => i.Items).SingleAsync(i => i.Key == s.Sale.InvoiceKey);
        var first = sale.Items.Single();
        var second = new SalesInvoiceItem
        {
            Key = Guid.NewGuid(), SalesInvoiceKey = sale.Key, ItemCode = "SOJA", ItemName = "SOJA EM GRAOS", UnitOfMeasureCode = "KG",
            Quantity = 5000m, UnitPrice = 2m, UsageCode = first.UsageCode, Cfop = first.Cfop, Ncm = first.Ncm,
            GoodsOrigin = 0, CstIcms = first.CstIcms, IcmsRate = first.IcmsRate, CstPis = first.CstPis,
            CstCofins = first.CstCofins, IbsCbsCst = first.IbsCbsCst, IbsCbsClassCode = first.IbsCbsClassCode,
            CbsRate = first.CbsRate, IbsStateRate = first.IbsStateRate, NfeItemNumber = 2,
        };
        // Key não é gerada: adicionar pela coleção rastreada vira Modified e estoura concorrência.
        s.Sale.Db.Context.SalesInvoicesItems.Add(second);
        await s.Sale.Db.SaveChangesAsync();

        var created = await NfeReturnTestSeed.CreateService(s.Sale.Db).ExecuteAsync(
            new SalesInvoiceNfeReturnRequest(s.Sale.InvoiceKey, [new SalesInvoiceNfeReturnItem(second.Key!.Value, 5000m)], "Recusa"),
            "tester");
        var sefaz = new FakeNfeSefazClient();
        sefaz.AuthorizeResponses.Enqueue(key => FakeNfeSefazClient.Authorized(key));

        await Issue(s.Sale, sefaz).ExecuteAsync(created.Key, "tester");

        var xml = await SignedXmlAsync(s.Sale, created.Key);
        Assert.Contains("<det nItem=\"1\">", xml);
        Assert.Contains("<nItem>2</nItem></DFeReferenciado>", xml);
    }

    [Fact]
    public async Task Balance_exceeded_at_issue_time_is_refused_before_reserving_a_number()
    {
        var s = await NfeReturnTestSeed.SeedAsync();
        var created = await NfeReturnTestSeed.CreateReturnAsync(s, 30000m);
        (await s.Sale.Db.Context.SalesInvoicesItems.SingleAsync(i => i.Key == s.SaleItemKey)).Quantity = 10000m;
        await s.Sale.Db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<DefaultException>(() => Issue(s.Sale, new FakeNfeSefazClient()).ExecuteAsync(created.Key, "tester"));

        Assert.Contains("passa do saldo devolvível da venda", ex.Message);
        Assert.Null((await s.Sale.Db.Context.SalesInvoices.AsNoTracking().SingleAsync(i => i.Key == created.Key)).TaxDocumentNumber);
    }

    [Fact]
    public async Task Sale_no_longer_confirmed_blocks_the_issue()
    {
        var s = await NfeReturnTestSeed.SeedAsync();
        var created = await NfeReturnTestSeed.CreateReturnAsync(s, 30000m);
        (await s.Sale.Db.Context.SalesInvoices.SingleAsync(i => i.Key == s.Sale.InvoiceKey)).InvoiceStatus = InvoiceStatus.Pending;
        await s.Sale.Db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<DefaultException>(() => Issue(s.Sale, new FakeNfeSefazClient()).ExecuteAsync(created.Key, "tester"));

        Assert.Contains("Venda de origem", ex.Message);
        Assert.Contains("não está confirmada", ex.Message);
    }

    [Fact]
    public async Task Own_return_does_not_need_a_payment_condition()
    {
        var s = await NfeReturnTestSeed.SeedAsync();
        var created = await NfeReturnTestSeed.CreateReturnAsync(s, 30000m);
        var invoice = await s.Sale.Db.Context.SalesInvoices.Include(i => i.Items).SingleAsync(i => i.Key == created.Key);

        var context = await new NfeReadinessValidator(s.Sale.Db, new NfeOptions(NfeTestSeed.Config())).ValidateAsync(invoice);

        Assert.Null(context.PaymentCondition);
        Assert.Equal(NfeReturnTestSeed.SaleAccessKey, context.ReturnOrigin!.AccessKey);
    }

    [Fact]
    public async Task Sale_date_in_the_reference_text_is_the_day_in_Brasilia_when_stored_in_UTC()
    {
        var s = await NfeReturnTestSeed.SeedAsync();
        var created = await NfeReturnTestSeed.CreateReturnAsync(s, 10000m);
        // 03/10 01:00 UTC = 02/10 22:00 em Brasília.
        (await s.Sale.Db.Context.SalesInvoices.SingleAsync(i => i.Key == s.Sale.InvoiceKey)).InvoiceDate = new DateTime(2026, 10, 3, 1, 0, 0);
        await s.Sale.Db.SaveChangesAsync();
        var sefaz = new FakeNfeSefazClient();
        sefaz.AuthorizeResponses.Enqueue(key => FakeNfeSefazClient.Authorized(key));

        await Issue(s.Sale, sefaz, TimeZoneInfo.Utc).ExecuteAsync(created.Key, "tester");

        var xml = await SignedXmlAsync(s.Sale, created.Key);
        Assert.Contains("de 02/10/2026, chave", xml);
    }
}
