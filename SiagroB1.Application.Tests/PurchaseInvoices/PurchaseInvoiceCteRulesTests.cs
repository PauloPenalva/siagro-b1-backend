using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.PurchaseInvoices;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.PurchaseInvoices;

/// <summary>Regras do CT-e de frete tomado no Documento de Entrada (spec 2026-10-17).</summary>
public class PurchaseInvoiceCteRulesTests
{
    private const string BranchTaxId = "12345678000195";

    // CT-e (modelo 57) emitido pelo fornecedor de teste (CPF 52998224725), número 456, série 1.
    private static string CteKey()
    {
        var first43 = "3526" + "10" + "00052998224725" + "57" + "001" + "000000456" + "1" + "12345678";
        return first43 + SupplierNfeKeyGuard.CheckDigit(first43);
    }

    private const string CteXml =
        "<?xml version=\"1.0\" encoding=\"utf-8\"?><cteProc xmlns=\"http://www.portalfiscal.inf.br/cte\" versao=\"4.00\">" +
        "<CTe><infCte versao=\"4.00\"><ide><mod>57</mod><serie>1</serie><nCT>456</nCT></ide></infCte></CTe></cteProc>";

    private static PurchaseInvoice Cte(int? usage, int lines = 1, bool withXml = false)
    {
        var invoice = new PurchaseInvoice
        {
            Key = Guid.NewGuid(), BranchCode = "01", CardCode = PurchaseNfeTestSeed.Supplier,
            IssuerType = DocumentIssuerType.ThirdParty, InvoiceType = PurchaseInvoiceType.Normal,
            TaxDocumentKind = TaxDocumentKind.Cte, ChaveNFe = CteKey(), TaxDocumentNumber = "456", TaxDocumentSeries = "1",
            IssueDate = new DateTime(2026, 10, 1), FreightStartState = "sp", FreightEndState = "pr",
            FreightPayerTaxId = "12.345.678/0001-95",
            XmlData = withXml ? System.Text.Encoding.UTF8.GetBytes(CteXml) : null,
        };
        for (var i = 0; i < lines; i++)
            invoice.AddItem(new PurchaseInvoiceItem
            {
                Key = Guid.NewGuid(), ItemCode = "TRIGO", UnitOfMeasureCode = "KG", Quantity = 1m, UnitPrice = 500m,
                UsageCode = usage,
            });
        return invoice;
    }

    private static PurchaseInvoicesCreateService Create(UnitOfWork db) =>
        new(db, PurchaseNfeTestSeed.Partners(), new FakeItemService(),
            TaxTestServices.PurchaseApply(db, PurchaseNfeTestSeed.Partners()), new FakeDocNumberSequenceService());

    private static PurchaseInvoicesUpdateService Update(UnitOfWork db) =>
        new(db, PurchaseNfeTestSeed.Partners(), new FakeItemService(),
            TaxTestServices.PurchaseApply(db, PurchaseNfeTestSeed.Partners()));

    private static async Task<string> RefusalAsync(UnitOfWork db, PurchaseInvoice invoice) =>
        (await Assert.ThrowsAsync<DefaultException>(() => Create(db).ExecuteAsync(invoice, "tester"))).Message;

    [Fact]
    public async Task Own_issuer_is_refused()
    {
        var db = (await PurchaseNfeTestSeed.SeedAsync()).Db;
        var invoice = Cte(await PurchaseNfeTestSeed.PurchaseUsageCodeAsync(db));
        invoice.IssuerType = DocumentIssuerType.Own;

        Assert.Equal("CT-e é sempre documento de terceiro.", await RefusalAsync(db, invoice));
    }

    [Fact]
    public async Task Non_normal_type_is_refused()
    {
        var db = (await PurchaseNfeTestSeed.SeedAsync()).Db;
        var invoice = Cte(await PurchaseNfeTestSeed.PurchaseUsageCodeAsync(db));
        invoice.InvoiceType = PurchaseInvoiceType.Return;

        Assert.Equal("CT-e só pode ser do tipo Normal.", await RefusalAsync(db, invoice));
    }

    [Theory]
    [InlineData(null, "PR")]
    [InlineData("SP", "")]
    [InlineData("SPP", "PR")]
    [InlineData("SP", "P")]
    public async Task Missing_or_malformed_state_is_refused(string? start, string? end)
    {
        var db = (await PurchaseNfeTestSeed.SeedAsync()).Db;
        var invoice = Cte(await PurchaseNfeTestSeed.PurchaseUsageCodeAsync(db));
        invoice.FreightStartState = start;
        invoice.FreightEndState = end;

        Assert.Equal("Informe a UF de início e a UF de fim da prestação do CT-e.", await RefusalAsync(db, invoice));
    }

    [Fact]
    public async Task Missing_payer_is_refused()
    {
        var db = (await PurchaseNfeTestSeed.SeedAsync()).Db;
        var invoice = Cte(await PurchaseNfeTestSeed.PurchaseUsageCodeAsync(db));
        invoice.FreightPayerTaxId = " ";

        Assert.Equal("Informe o CNPJ do tomador do CT-e.", await RefusalAsync(db, invoice));
    }

    [Fact]
    public async Task Payer_that_is_not_the_branch_is_refused()
    {
        var db = (await PurchaseNfeTestSeed.SeedAsync()).Db;
        var invoice = Cte(await PurchaseNfeTestSeed.PurchaseUsageCodeAsync(db));
        invoice.FreightPayerTaxId = "11222333000181";

        Assert.Equal("O tomador do CT-e (CNPJ 11222333000181) não é a filial do documento.", await RefusalAsync(db, invoice));
    }

    [Fact]
    public async Task Cte_with_more_than_one_line_is_refused()
    {
        var db = (await PurchaseNfeTestSeed.SeedAsync()).Db;
        var invoice = Cte(await PurchaseNfeTestSeed.PurchaseUsageCodeAsync(db), 2);

        Assert.Equal("O CT-e tem uma única linha: o serviço de transporte.", await RefusalAsync(db, invoice));
    }

    [Fact]
    public async Task Cte_with_xml_saves_without_reading_it_as_nfe()
    {
        var db = (await PurchaseNfeTestSeed.SeedAsync()).Db;
        var invoice = Cte(await PurchaseNfeTestSeed.PurchaseUsageCodeAsync(db), withXml: true);

        await Create(db).ExecuteAsync(invoice, "tester");

        var saved = await db.Context.PurchaseInvoices.AsNoTracking().SingleAsync(i => i.Key == invoice.Key);
        Assert.Equal(("SP", "PR", BranchTaxId), (saved.FreightStartState, saved.FreightEndState, saved.FreightPayerTaxId));
        Assert.Equal(TaxDocumentKind.Cte, saved.TaxDocumentKind);
    }

    [Fact]
    public async Task Update_copies_cte_header_fields()
    {
        var db = (await PurchaseNfeTestSeed.SeedAsync()).Db;
        var invoice = Cte(await PurchaseNfeTestSeed.PurchaseUsageCodeAsync(db));
        await Create(db).ExecuteAsync(invoice, "tester");
        db.Context.ChangeTracker.Clear();

        var changed = await db.Context.PurchaseInvoices.AsNoTracking().Include(i => i.Items).SingleAsync(i => i.Key == invoice.Key);
        changed.FreightStartState = "mg";
        changed.FreightEndState = "go";
        changed.FreightStartCity = "UBERABA";
        changed.FreightEndCity = "GOIANIA";
        changed.FreightPayerTaxId = "12345678000195";
        changed.CteIcmsCst = "00";
        changed.CteIcmsValue = 60m;

        await Update(db).ExecuteAsync(changed.Key, changed, "tester");

        var saved = await db.Context.PurchaseInvoices.AsNoTracking().SingleAsync(i => i.Key == invoice.Key);
        Assert.Equal(("MG", "GO", "UBERABA", "GOIANIA", BranchTaxId, "00", 60m),
            (saved.FreightStartState, saved.FreightEndState, saved.FreightStartCity, saved.FreightEndCity,
                saved.FreightPayerTaxId, saved.CteIcmsCst, saved.CteIcmsValue));
    }

    [Fact]
    public async Task Update_refuses_a_payer_that_is_not_the_branch()
    {
        var db = (await PurchaseNfeTestSeed.SeedAsync()).Db;
        var invoice = Cte(await PurchaseNfeTestSeed.PurchaseUsageCodeAsync(db));
        await Create(db).ExecuteAsync(invoice, "tester");
        db.Context.ChangeTracker.Clear();

        var changed = await db.Context.PurchaseInvoices.AsNoTracking().Include(i => i.Items).SingleAsync(i => i.Key == invoice.Key);
        changed.FreightPayerTaxId = "11222333000181";

        var e = await Assert.ThrowsAsync<DefaultException>(() => Update(db).ExecuteAsync(changed.Key, changed, "tester"));

        Assert.Equal("O tomador do CT-e (CNPJ 11222333000181) não é a filial do documento.", e.Message);
    }

    [Fact]
    public async Task Confirm_refuses_a_cte_whose_payer_is_not_the_branch()
    {
        var db = (await PurchaseNfeTestSeed.SeedAsync()).Db;
        var invoice = Cte(await PurchaseNfeTestSeed.PurchaseUsageCodeAsync(db));
        invoice.FreightPayerTaxId = "11222333000181";
        db.Context.PurchaseInvoices.Add(invoice);
        await db.SaveChangesAsync();

        var confirm = TaxTestServices.PurchaseConfirm(db, "STANDALONE", PurchaseNfeTestSeed.Partners(), new FakeNfeSefazClient());
        var e = await Assert.ThrowsAsync<DefaultException>(() => confirm.ExecuteAsync(invoice.Key, "tester"));

        Assert.Equal("O tomador do CT-e (CNPJ 11222333000181) não é a filial do documento.", e.Message);
    }
}
