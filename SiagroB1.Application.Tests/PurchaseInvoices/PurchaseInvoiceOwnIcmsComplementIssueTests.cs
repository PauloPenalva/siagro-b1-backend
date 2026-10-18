using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Nfe;
using SiagroB1.Application.Services.Taxes;
using SiagroB1.Application.Tests.Nfe;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Fiscal.Nfe;
using SiagroB1.Fiscal.Payments;
using static SiagroB1.Application.Tests.Support.ValueComplementNfeSeed;

namespace SiagroB1.Application.Tests.PurchaseInvoices;

/// <summary>
/// Spec 2026-10-17 D2, D8, D9 e "Emissão (próprio)": o complemento de ICMS próprio sai como NF-e complementar
/// (finNFe=2), sem pagamento (tPag 90), sem pesos, referenciando a original; sem devolução e travando o cancelamento dela.
/// </summary>
public class PurchaseInvoiceOwnIcmsComplementIssueTests
{
    private static readonly XNamespace Nfe = "http://www.portalfiscal.inf.br/nfe";
    private const string C6 = "A nota original DE000005 não está mais autorizada: o complemento não pode ser emitido.";
    private const string C8 = "A nota DE000005 tem o complemento ST-0001 ativo: cancele o complemento antes.";
    private const string K2 = "Item TRIGO: informe a base e o valor do ICMS do complemento.";
    private const string K6 = "Complemento de ICMS não tem devolução.";

    /// <summary>Complemento de ICMS próprio gravado: sem condição de pagamento e sem pesos.</summary>
    private static async Task<PurchaseInvoice> IcmsComplementAsync(ValueComplementScenario s)
    {
        var invoice = s.Complement((s.OriginalItemKey, 0m));
        invoice.InvoiceType = PurchaseInvoiceType.IcmsComplement;
        invoice.PaymentConditionCode = null;
        var line = invoice.Items.Single();
        line.IcmsBase = 1_000m;
        line.IcmsValue = 120m;

        await Create(s.Db).ExecuteAsync(invoice, "ana");
        return invoice;
    }

    private static NfeReadinessValidator Readiness(ValueComplementScenario s) =>
        new(s.Db, new NfeOptions(NfeTestSeed.Config()));

    private static Task<PurchaseInvoice> LoadAsync(ValueComplementScenario s, Guid key) =>
        s.Db.Context.PurchaseInvoices.Include(i => i.Items).SingleAsync(i => i.Key == key);

    [Fact]
    public async Task Own_icms_complement_is_issuable_without_payment_condition_or_weights()
    {
        var s = await SeedAsync();
        var complement = await IcmsComplementAsync(s);
        var invoice = await LoadAsync(s, complement.Key);
        Assert.Null(invoice.PaymentConditionCode);
        Assert.Equal((0m, 0m), (invoice.GrossWeight, invoice.NetWeight));

        var context = await Readiness(s).ValidateAsync(invoice);
        invoice.TaxDocumentNumber = "000000010";
        invoice.TaxDocumentSeries = "9";
        invoice.NfeRandomCode = "12345678";
        var input = NfeIssueInputAssembler.Build(invoice, context, NfeTestSeed.Clock(), null);

        Assert.Equal(NfePurpose.Complement, input.Purpose);
        Assert.Equal(PaymentMeansCodes.NoPayment, input.Payment.PaymentMeans);
        Assert.Equal(0m, input.Payment.PaidAmount);
        Assert.Equal([AccessKey(5)], input.ReferencedKeys);
    }

    [Fact]
    public async Task Own_icms_complement_is_issued_as_a_complementary_nfe_without_payment()
    {
        var s = await SeedAsync();
        var invoice = await IcmsComplementAsync(s);
        var sefaz = new FakeNfeSefazClient();
        sefaz.AuthorizeResponses.Enqueue(key => FakeNfeSefazClient.Authorized(key));

        var outcome = await PurchaseInvoicesNfeIssueServiceTests.Issue(s.Purchase, sefaz).ExecuteAsync(invoice.Key, "tester");

        Assert.Equal(NfeStatus.Authorized, outcome.NfeStatus);
        var xml = XDocument.Parse(Assert.Single(sefaz.Sent).Xml);
        var ide = xml.Descendants(Nfe + "ide").Single();
        Assert.Equal("2", ide.Element(Nfe + "finNFe")!.Value);
        Assert.Equal(AccessKey(5), ide.Elements(Nfe + "NFref").Single().Element(Nfe + "refNFe")!.Value);
        Assert.Equal("90", xml.Descendants(Nfe + "detPag").Single().Element(Nfe + "tPag")!.Value);
        Assert.Empty(xml.Descendants(Nfe + "cobr"));
        Assert.Equal("9", xml.Descendants(Nfe + "modFrete").Single().Value);
        Assert.Empty(xml.Descendants(Nfe + "vol"));
        Assert.Equal("0.00", xml.Descendants(Nfe + "prod").Single().Element(Nfe + "vProd")!.Value);
        var total = xml.Descendants(Nfe + "ICMSTot").Single();
        Assert.Equal("1000.00", total.Element(Nfe + "vBC")!.Value);
        Assert.Equal("120.00", total.Element(Nfe + "vICMS")!.Value);
        Assert.Equal("0.00", total.Element(Nfe + "vNF")!.Value);
    }

    [Fact]
    public async Task Own_icms_complement_readiness_refuses_line_without_icms()
    {
        var s = await SeedAsync();
        var complement = await IcmsComplementAsync(s);
        // Gravado fora do serviço: a regra das linhas recusaria na gravação (K2).
        await ChangeAsync(s.Db, complement.Key, c => c.Items.Single().IcmsBase = 0m);
        var invoice = await LoadAsync(s, complement.Key);

        var e = await Assert.ThrowsAsync<DefaultException>(() => Readiness(s).ValidateAsync(invoice));

        Assert.Equal(K2, e.Message);
    }

    [Fact]
    public async Task Own_icms_complement_refuses_original_no_longer_authorized()
    {
        var s = await SeedAsync();
        var invoice = await IcmsComplementAsync(s);
        await ChangeAsync(s.Db, s.OriginalKey, o =>
        {
            o.NfeStatus = NfeStatus.Cancelled;
            o.InvoiceStatus = InvoiceStatus.Cancelled;
        });
        var sefaz = new FakeNfeSefazClient();
        var reservation = new FakeNfeNumberReservationService();

        var e = await Assert.ThrowsAsync<DefaultException>(() =>
            PurchaseInvoicesNfeIssueServiceTests.Issue(s.Purchase, sefaz, reservation).ExecuteAsync(invoice.Key, "tester"));

        Assert.Equal(C6, e.Message);
        Assert.Empty(sefaz.Sent);
        Assert.Equal(0, reservation.Calls);
    }

    [Fact]
    public async Task Cancelling_original_with_active_icms_complement_is_refused()
    {
        var s = await SeedAsync();
        await IcmsComplementAsync(s);
        var sefaz = new FakeNfeSefazClient();

        var nfeCancel = await Assert.ThrowsAsync<DefaultException>(() =>
            NfeCancelTestServices.PurchaseCancel(s.Db, sefaz).ExecuteAsync(s.OriginalKey, "Entrada lancada em duplicidade", "tester"));
        var documentCancel = await Assert.ThrowsAsync<DefaultException>(() =>
            FinancialDocumentTestServices.PurchaseCancel(s.Db).EnsureCanCancelAsync(s.OriginalKey));

        Assert.Equal(C8, nfeCancel.Message);
        Assert.Equal(C8, documentCancel.Message);
        Assert.Empty(sefaz.CancelRequests);
    }

    [Fact]
    public async Task Icms_complement_has_no_return()
    {
        var s = await SeedAsync();
        var complement = await IcmsComplementAsync(s);
        var returns = new PurchaseInvoicesNfeReturnCreateService(
            s.Db, new TaxCalculationGate(s.Db, NfeTestSeed.Config()), Create(s.Db), NfeTestSeed.Clock,
            NfeIssueInputAssembler.BrasiliaZone);

        var e = await Assert.ThrowsAsync<DefaultException>(() => returns.ExecuteAsync(
            new PurchaseInvoiceNfeReturnRequest(
                complement.Key, [new PurchaseInvoiceNfeReturnItem(complement.Items.Single().Key!.Value, 1m)], "grão fora do padrão"),
            "tester"));

        Assert.Equal(K6, e.Message);
    }
}
