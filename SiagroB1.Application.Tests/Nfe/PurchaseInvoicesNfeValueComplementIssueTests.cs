using System.Globalization;
using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Nfe;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using static SiagroB1.Application.Tests.Support.ValueComplementNfeSeed;

namespace SiagroB1.Application.Tests.Nfe;

/// <summary>Spec 2026-10-14, regras 6–12: o complemento de valor próprio sai como NF-e complementar (finNFe=2).</summary>
public class PurchaseInvoicesNfeValueComplementIssueTests
{
    private static readonly XNamespace Nfe = "http://www.portalfiscal.inf.br/nfe";

    private static async Task<PurchaseInvoice> CreateAsync(
        ValueComplementScenario s, params (Guid? OriginalItemKey, decimal Amount)[] lines)
    {
        var invoice = s.Complement(lines);
        await Create(s.Db).ExecuteAsync(invoice, "ana");
        return invoice;
    }

    private static decimal Value(XElement prod, string name) =>
        decimal.Parse(prod.Element(Nfe + name)!.Value, CultureInfo.InvariantCulture);

    [Fact]
    public async Task Own_complement_is_taxed_like_the_own_entry()
    {
        var s = await SeedAsync();

        var invoice = await CreateAsync(s, (s.OriginalItemKey, 300m));

        var line = await s.Db.Context.PurchaseInvoicesItems.AsNoTracking().SingleAsync(l => l.PurchaseInvoiceKey == invoice.Key);
        Assert.Equal("1102", line.Cfop);
        Assert.Equal("51", line.CstIcms);
        Assert.Equal(300m, line.IcmsBase);
        // O nome da natureza (natOp da NF-e, regra 12) é preenchido na linha do complemento.
        Assert.Equal("COMPRA DE MERCADORIA", line.UsageName);
    }

    [Fact]
    public async Task Own_complement_is_issued_as_a_complementary_nfe_referencing_the_originals()
    {
        var s = await SeedAsync();
        var (_, second) = await AddOriginalAsync(s.Db, s.Contract, s.PurchaseUsage, SecondNumber, 7, items: 2);
        // nItem = ordem das chaves (NextLineKey crescente): 1ª linha → nota 7, 2ª → nota 5, 3ª → nota 7 de novo.
        var invoice = await CreateAsync(s, (second[0], 300m), (s.OriginalItemKey, 200m), (second[1], 100m));
        var sefaz = new FakeNfeSefazClient();
        sefaz.AuthorizeResponses.Enqueue(key => FakeNfeSefazClient.Authorized(key));

        var outcome = await PurchaseInvoicesNfeIssueServiceTests.Issue(s.Purchase, sefaz).ExecuteAsync(invoice.Key, "tester");

        Assert.Equal(NfeStatus.Authorized, outcome.NfeStatus);
        var xml = XDocument.Parse(Assert.Single(sefaz.Sent).Xml);
        var ide = xml.Descendants(Nfe + "ide").Single();
        Assert.Equal("2", ide.Element(Nfe + "finNFe")!.Value);
        Assert.Equal("0", ide.Element(Nfe + "tpNF")!.Value);
        // Chaves DISTINTAS, na ordem de primeira aparição.
        Assert.Equal([AccessKey(7), AccessKey(5)],
            ide.Elements(Nfe + "NFref").Select(r => r.Element(Nfe + "refNFe")!.Value).ToArray());
        Assert.Empty(xml.Descendants(Nfe + "DFeReferenciado"));

        var products = xml.Descendants(Nfe + "det").Select(d => d.Element(Nfe + "prod")!).ToList();
        Assert.Equal([300m, 200m, 100m], products.Select(p => Value(p, "vProd")).ToArray());
        foreach (var prod in products)
        {
            Assert.Equal(0m, Value(prod, "qCom"));
            Assert.Equal(0m, Value(prod, "vUnCom"));
            Assert.Equal(0m, Value(prod, "qTrib"));
            Assert.Equal(0m, Value(prod, "vUnTrib"));
            Assert.Equal("KG", prod.Element(Nfe + "uCom")!.Value);
            Assert.Equal("KG", prod.Element(Nfe + "uTrib")!.Value);
        }

        // Só valor: sem pesos, o montador omite o vol.
        Assert.Empty(xml.Descendants(Nfe + "vol"));
        // natOp = o da natureza da linha (regra 12).
        Assert.Equal("COMPRA DE MERCADORIA", ide.Element(Nfe + "natOp")!.Value);

        // Emitir é confirmar: a confirmação cria um firme por linha (regressão do sub-projeto 2).
        var saved = await PurchaseInvoicesNfeIssueServiceTests.ReloadAsync(s.Purchase, invoice.Key);
        Assert.Equal(InvoiceStatus.Confirmed, saved.InvoiceStatus);
        var lineKeys = saved.Items.Select(i => (Guid?)i.Key!.Value).ToList();
        Assert.Equal(3, await s.Db.Context.FinancialDocuments.AsNoTracking()
            .CountAsync(d => d.OriginType == FinancialDocumentOrigin.PurchaseInvoice && lineKeys.Contains(d.OriginKey)));
    }

    [Fact]
    public async Task Cancelled_original_blocks_the_emission()
    {
        var s = await SeedAsync();
        var invoice = await CreateAsync(s, (s.OriginalItemKey, 300m));
        await ChangeAsync(s.Db, s.OriginalKey, o =>
        {
            o.NfeStatus = NfeStatus.Cancelled;
            o.InvoiceStatus = InvoiceStatus.Cancelled;
        });
        var sefaz = new FakeNfeSefazClient();

        var e = await Assert.ThrowsAsync<DefaultException>(() =>
            PurchaseInvoicesNfeIssueServiceTests.Issue(s.Purchase, sefaz).ExecuteAsync(invoice.Key, "tester"));

        Assert.Equal("A nota original DE000005 não está mais autorizada: o complemento não pode ser emitido.", e.Message);
        Assert.Empty(sefaz.Sent);
    }

    [Fact]
    public async Task Third_party_complement_is_still_not_issued()
    {
        var s = await SeedAsync();
        var invoice = s.Complement((null, 300m));
        invoice.IssuerType = DocumentIssuerType.ThirdParty;
        invoice.InvoiceStatus = InvoiceStatus.Pending;
        s.Db.Context.PurchaseInvoices.Add(invoice);
        await s.Db.SaveChangesAsync();

        var e = await Assert.ThrowsAsync<DefaultException>(() =>
            PurchaseInvoicesNfeIssueServiceTests.Issue(s.Purchase, new FakeNfeSefazClient()).ExecuteAsync(invoice.Key, "tester"));

        Assert.Equal("Só o documento de emissão própria é emitido como NF-e.", e.Message);
    }

    [Fact]
    public async Task Own_entry_without_weights_is_still_refused()
    {
        var s = await SeedAsync();
        await ChangeAsync(s.Db, s.Purchase.InvoiceKey, i => { i.GrossWeight = 0m; i.NetWeight = 0m; });
        var invoice = await s.Db.Context.PurchaseInvoices.Include(i => i.Items).SingleAsync(i => i.Key == s.Purchase.InvoiceKey);

        var e = await Assert.ThrowsAsync<DefaultException>(() =>
            new NfeReadinessValidator(s.Db, new NfeOptions(NfeTestSeed.Config())).ValidateAsync(invoice));

        Assert.Contains("Documento: peso bruto e peso líquido", e.Message);
    }
}
