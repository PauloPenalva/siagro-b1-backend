using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Services;
using static SiagroB1.Application.Tests.Support.InvoiceReportSeed;

namespace SiagroB1.Application.Tests.Reports;

public class FiscalConferenceServiceTests
{
    private const string Key44 = "35260712345678000195550010000001231000001230";

    [Fact]
    public async Task Build_ConfirmedSaleAndPurchaseOfTheMonthAreListed()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.SalesInvoices.Add(OwnSale("V1"));
        db.Context.PurchaseInvoices.Add(Purchase("C1", items: [PurchaseLine("1101")]));
        await Save(db);

        var data = await Service(db).BuildAsync(Request());

        Assert.Equal(2, data.Documents.Count);
        Assert.Equal(new[] { "E", "S" }, data.Documents.Select(d => d.Direction).Order());
    }

    [Fact]
    public async Task Build_PendingAndCancelledAreLeftOut()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.SalesInvoices.Add(OwnSale("PEND", status: InvoiceStatus.Pending));
        db.Context.SalesInvoices.Add(OwnSale("CANC", status: InvoiceStatus.Cancelled));
        db.Context.SalesInvoices.Add(OwnSale("RET", status: InvoiceStatus.Returned));
        db.Context.PurchaseInvoices.Add(Purchase("PEND", status: InvoiceStatus.Pending, items: [PurchaseLine("1101")]));
        db.Context.PurchaseInvoices.Add(Purchase("CANC", status: InvoiceStatus.Cancelled, items: [PurchaseLine("1101")]));
        db.Context.PurchaseInvoices.Add(Purchase("RET", status: InvoiceStatus.Returned, items: [PurchaseLine("1101")]));
        await Save(db);

        var data = await Service(db).BuildAsync(Request());

        Assert.Equal(new[] { "RET", "RET" }, data.Documents.Select(d => d.InternalNumber));
    }

    [Theory]
    [InlineData(NfeStatus.Cancelled)]
    [InlineData(NfeStatus.Denied)]
    [InlineData(NfeStatus.Voided)]
    public async Task Build_NfeCancelledDeniedOrVoidedIsLeftOut(NfeStatus nfe)
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.SalesInvoices.Add(OwnSale("S", nfe: nfe));
        db.Context.PurchaseInvoices.Add(Purchase("P", items: [PurchaseLine("1101")]).With(p => p.NfeStatus = nfe));
        await Save(db);

        var data = await Service(db).BuildAsync(Request());

        Assert.Empty(data.Documents);
    }

    [Fact]
    public async Task Build_SaleWithoutTaxDocumentIsLeftOut()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.SalesInvoices.Add(OwnSale("SEM").With(s => s.WithoutTaxDocument = true));
        db.Context.SalesInvoices.Add(OwnSale("COM"));
        await Save(db);

        var data = await Service(db).BuildAsync(Request());

        Assert.Equal(new[] { "COM" }, data.Documents.Select(d => d.InternalNumber));
    }

    [Fact]
    public async Task Build_NfseAndCteAreLeftOut()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.SalesInvoices.Add(OwnSale("NFSE").With(s => s.TaxDocumentKind = TaxDocumentKind.Nfse));
        db.Context.SalesInvoices.Add(OwnSale("CTE").With(s => s.TaxDocumentKind = TaxDocumentKind.Cte));
        db.Context.PurchaseInvoices.Add(Purchase("NFSE", items: [PurchaseLine("1101")]).With(p => p.TaxDocumentKind = TaxDocumentKind.Nfse));
        db.Context.PurchaseInvoices.Add(Purchase("CTE", items: [PurchaseLine("1101")]).With(p => p.TaxDocumentKind = TaxDocumentKind.Cte));
        await Save(db);

        var data = await Service(db).BuildAsync(Request());

        Assert.Empty(data.Documents);
    }

    [Fact]
    public async Task Build_PurchaseUsesPostingDate()
    {
        var db = TestDb.CreateUnitOfWork();
        var late = Purchase("LATE", items: [PurchaseLine("1101")]);
        late.IssueDate = new DateTime(2026, 7, 30);
        late.PostingDate = new DateTime(2026, 8, 2);
        var noPosting = Purchase("NOPOST", items: [PurchaseLine("1101")]);
        noPosting.IssueDate = new DateTime(2026, 7, 10);
        noPosting.PostingDate = null;
        db.Context.PurchaseInvoices.AddRange(late, noPosting);
        await Save(db);

        var july = await Service(db).BuildAsync(Request());
        var august = await Service(db).BuildAsync(new FiscalConferenceRequest { Year = 2026, Month = 8 });

        Assert.Equal(new[] { "NOPOST" }, july.Documents.Select(d => d.InternalNumber));
        Assert.Equal(new[] { "LATE" }, august.Documents.Select(d => d.InternalNumber));
    }

    [Fact]
    public async Task Build_BranchFilterRestricts()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.SalesInvoices.Add(OwnSale("F01"));
        db.Context.SalesInvoices.Add(OwnSale("F02", branch: "02"));
        db.Context.PurchaseInvoices.Add(Purchase("P02", branchCode: "02", items: [PurchaseLine("1101")]));
        await Save(db);

        var filtered = await Service(db).BuildAsync(new FiscalConferenceRequest { BranchCode = "01", Year = 2026, Month = 7 });
        var all = await Service(db).BuildAsync(Request());

        Assert.Equal(new[] { "F01" }, filtered.Documents.Select(d => d.InternalNumber));
        Assert.Equal(3, all.Documents.Count);
    }

    [Fact]
    public async Task Build_EmptyMonthReturnsEmptyListsAndZeroTotals()
    {
        var db = TestDb.CreateUnitOfWork();

        var data = await Service(db).BuildAsync(Request());

        Assert.Empty(data.Documents);
        Assert.Empty(data.Summary);
        Assert.Empty(data.Assessment);
        Assert.Empty(data.R2055);
        Assert.Empty(data.Issues);
        Assert.Equal(0m, data.AssessmentTotals.Debits);
        Assert.Equal(0m, data.AssessmentTotals.Credits);
        Assert.Equal(0m, data.AssessmentTotals.Balance);
    }

    [Fact]
    public async Task Build_ModelIs55ForNfeAnd01ForOther()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.SalesInvoices.Add(OwnSale("NFE"));
        db.Context.SalesInvoices.Add(OwnSale("OUT").With(s => s.TaxDocumentKind = TaxDocumentKind.Other));
        await Save(db);

        var data = await Service(db).BuildAsync(Request());

        Assert.Equal("55", data.Documents.Single(d => d.InternalNumber == "NFE").Model);
        Assert.Equal("01", data.Documents.Single(d => d.InternalNumber == "OUT").Model);
    }

    [Fact]
    public async Task Build_OwnNfeNotAuthorizedIsLeftOutAndReturnedApart()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.SalesInvoices.Add(OwnSale("NAO", nfe: NfeStatus.None));
        db.Context.PurchaseInvoices.Add(Purchase("PROPRIA", issuer: DocumentIssuerType.Own, items: [PurchaseLine("5202")]));
        db.Context.PurchaseInvoices.Add(Purchase("TERCEIRO", items: [PurchaseLine("1101")]));
        await Save(db);

        var (included, unauthorized) = await Service(db).LoadLinesAsync(Request());

        Assert.Equal(new[] { "TERCEIRO" }, included.Select(d => d.InternalNumber));
        Assert.Equal(new[] { "NAO", "PROPRIA" }, unauthorized.Select(d => d.InternalNumber).Order());
    }

    [Fact]
    public async Task Build_LineCarriesStoredValuesAndCfopDecidesDirection()
    {
        var db = TestDb.CreateUnitOfWork();
        var line = PurchaseLine("5202");
        line.CstIcms = "00"; line.IcmsBase = 1000m; line.IcmsRate = 12m; line.IcmsValue = 120m;
        line.CstPis = "01"; line.PisValue = 6.5m; line.CstCofins = "01"; line.CofinsValue = 30m;
        db.Context.PurchaseInvoices.Add(Purchase("DEV", issuer: DocumentIssuerType.Own, items: [line])
            .With(p => { p.NfeStatus = NfeStatus.Authorized; p.ChaveNFe = Key44; }));
        await Save(db);

        var row = (await Service(db).BuildAsync(Request())).Documents.Single();

        Assert.Equal("S", row.Direction);
        Assert.Equal("Entrada", row.DocumentType);
        Assert.Equal(Key44, row.AccessKey);
        Assert.Equal("5202", row.Cfop);
        Assert.Equal("00", row.CstIcms);
        Assert.Equal(1000m, row.OperationValue);
        Assert.Equal(1000m, row.IcmsBase);
        Assert.Equal(12m, row.IcmsRate);
        Assert.Equal(120m, row.IcmsValue);
        Assert.Equal(6.5m, row.PisValue);
        Assert.Equal(30m, row.CofinsValue);
    }

    [Fact]
    public async Task Build_EmptyCfopFallsBackToDocumentDirection()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.SalesInvoices.Add(OwnSale("S", cfop: null));
        db.Context.PurchaseInvoices.Add(Purchase("P", items: [PurchaseLine(null)]));
        await Save(db);

        var data = await Service(db).BuildAsync(Request());

        Assert.Equal("S", data.Documents.Single(d => d.InternalNumber == "S").Direction);
        Assert.Equal("E", data.Documents.Single(d => d.InternalNumber == "P").Direction);
    }

    [Fact]
    public async Task Build_SummaryGroupsByDirectionCstCfopAndRate()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.SalesInvoices.Add(OwnSale("V1", icms: Icms("00", 12m, 1000m, 120m)));
        db.Context.SalesInvoices.Add(OwnSale("V2", icms: Icms("00", 12m, 500m, 60m)));
        db.Context.SalesInvoices.Add(OwnSale("V3", icms: Icms("00", 7m, 200m, 14m)));
        db.Context.PurchaseInvoices.Add(Purchase("C1", items: [PurchaseLine("1101", Icms("00", 12m, 300m, 36m))]));
        await Save(db);

        var summary = (await Service(db).BuildAsync(Request())).Summary;

        Assert.Equal(3, summary.Count);
        Assert.Equal(new[] { "E", "S", "S" }, summary.Select(r => r.Direction));
        var e = summary[0];
        Assert.Equal(("1101", "00", 12m, 300m, 36m), (e.Cfop, e.CstIcms, e.IcmsRate, e.IcmsBase, e.IcmsValue));
        var s12 = summary.Single(r => r.Direction == "S" && r.IcmsRate == 12m);
        Assert.Equal(1500m, s12.IcmsBase);
        Assert.Equal(180m, s12.IcmsValue);
        Assert.Equal(1500m, s12.OperationValue);
        Assert.Equal(200m, summary.Single(r => r.IcmsRate == 7m).IcmsBase);
        Assert.Equal(7m, summary[1].IcmsRate);
    }

    [Fact]
    public async Task Build_AssessmentDebitsOutgoingAndCreditsIncoming()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.SalesInvoices.Add(OwnSale("V1", icms: Icms("00", 12m, 1000m, 120m)));
        db.Context.PurchaseInvoices.Add(Purchase("C1", items: [PurchaseLine("1101", Icms("00", 7m, 1000m, 70m))]));
        await Save(db);

        var data = await Service(db).BuildAsync(Request());

        Assert.Equal(120m, data.AssessmentTotals.Debits);
        Assert.Equal(70m, data.AssessmentTotals.Credits);
        Assert.Equal(50m, data.AssessmentTotals.Balance);
        Assert.Equal(new[] { "1101", "5101" }, data.Assessment.Select(a => a.Cfop));
        Assert.Equal(70m, data.Assessment[0].Credit);
        Assert.Equal(0m, data.Assessment[0].Debit);
        Assert.Equal(120m, data.Assessment[1].Debit);
    }

    [Fact]
    public async Task Build_OwnPurchaseReturnWithOutgoingCfopIsDebit()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.PurchaseInvoices.Add(Purchase("DEV", type: PurchaseInvoiceType.Return, issuer: DocumentIssuerType.Own,
                items: [PurchaseLine("5202", Icms("00", 12m, 250m, 30m))])
            .With(p => { p.NfeStatus = NfeStatus.Authorized; p.ChaveNFe = Key44; }));
        await Save(db);

        var data = await Service(db).BuildAsync(Request());

        Assert.Equal(30m, data.AssessmentTotals.Debits);
        Assert.Equal(0m, data.AssessmentTotals.Credits);
        Assert.Equal(30m, data.AssessmentTotals.Balance);
    }

    [Fact]
    public async Task Build_ComplementCountsInAssessment()
    {
        var db = TestDb.CreateUnitOfWork();
        var sale = OwnSale("COMPL", icms: Icms("90", 0m, 0m, 10m));
        sale.InvoiceType = SalesInvoiceType.IcmsComplement;
        db.Context.SalesInvoices.Add(sale);
        await Save(db);

        var data = await Service(db).BuildAsync(Request());

        Assert.Equal(10m, data.AssessmentTotals.Debits);
    }

    private static async Task<List<FiscalIssueDto>> IssuesOf(IUnitOfWork db, string rule) =>
        (await Service(db).BuildAsync(Request())).Issues.Where(i => i.Rule == rule).ToList();

    [Fact]
    public async Task Build_R2055GroupsByProducer()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.PurchaseInvoices.Add(Purchase("A1", cardCode: "F001", cardName: "ZE", items: Withheld(1000m)));
        db.Context.PurchaseInvoices.Add(Purchase("A2", cardCode: "F001", cardName: "ZE", items: Withheld(500m)));
        db.Context.PurchaseInvoices.Add(Purchase("B1", cardCode: "F002", cardName: "ANA", items: Withheld(200m)));
        await Save(db);

        var rows = (await Service(db).BuildAsync(Request())).R2055;

        Assert.Equal(new[] { "F002", "F001" }, rows.Select(r => r.CardCode));
        var ze = rows.Single(r => r.CardCode == "F001");
        Assert.Equal((1500m, 18m, 1.5m, 3m, 22.5m), (ze.Base, ze.Inss, ze.Rat, ze.Senar, ze.Total));
        Assert.Equal(200m, rows.Single(r => r.CardCode == "F002").Base);
    }

    [Fact]
    public async Task Build_R2055DeductsReturns()
    {
        var db = TestDb.CreateUnitOfWork();
        var origin = Withheld(1000m);
        db.Context.PurchaseInvoices.Add(Purchase("A1", items: origin));
        db.Context.PurchaseInvoices.Add(Return("D1", new DateTime(2026, 7, 20), origin, 400m));
        await Save(db);

        var row = (await Service(db).BuildAsync(Request())).R2055.Single();

        Assert.Equal("F001", row.CardCode);
        Assert.Equal((600m, 7.20m), (row.Base, row.Inss));
    }

    [Fact]
    public async Task Build_R2055UsesRuralProducerCpfWhenPresent()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.BusinessPartners.Add(new BusinessPartner
        {
            CardCode = "F001", CardName = "ZE", TaxId = "12345678000195",
            RuralProducerCpf = "11122233344", RuralProducerType = RuralProducerType.SpecialInsured,
        });
        db.Context.BusinessPartners.Add(new BusinessPartner { CardCode = "F002", CardName = "ANA", TaxId = "99988877766" });
        db.Context.PurchaseInvoices.Add(Purchase("A1", cardCode: "F001", items: Withheld(1000m)));
        db.Context.PurchaseInvoices.Add(Purchase("A2", cardCode: "F002", items: Withheld(1000m)));
        db.Context.PurchaseInvoices.Add(Purchase("A3", cardCode: "F003", items: Withheld(1000m)));
        await Save(db);

        var rows = (await Service(db).BuildAsync(Request())).R2055;

        var withCpf = rows.Single(r => r.CardCode == "F001");
        Assert.Equal(("11122233344", "Segurado especial"), (withCpf.TaxId, withCpf.ProducerType));
        var noCpf = rows.Single(r => r.CardCode == "F002");
        Assert.Equal(("99988877766", ""), (noCpf.TaxId, noCpf.ProducerType));
        var missing = rows.Single(r => r.CardCode == "F003");
        Assert.Equal(("", ""), (missing.TaxId, missing.ProducerType));
    }

    private static PurchaseInvoiceItem CleanPurchaseLine(string? cfop = "1101")
    {
        var line = PurchaseLine(cfop);
        line.CstIcms = "00";
        return line;
    }

    private static SalesInvoice CleanSale(string number, string? cfop = "5101")
    {
        var sale = OwnSale(number, cfop: cfop);
        sale.Items.First().CstIcms = "00";
        return sale;
    }

    [Fact]
    public async Task Build_IssueItemWithoutCfop()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.SalesInvoices.Add(CleanSale("V1", cfop: null));
        db.Context.SalesInvoices.Add(CleanSale("V2"));
        await Save(db);

        var issue = Assert.Single(await IssuesOf(db, "Item sem CFOP"));

        Assert.Equal("V1", issue.InternalNumber);
        Assert.Equal("10001", issue.ItemCode);
        Assert.Equal("S", issue.Direction);
    }

    [Fact]
    public async Task Build_IssueItemWithoutCstIcms()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.PurchaseInvoices.Add(Purchase("SEM", items: [PurchaseLine("1101")]));
        db.Context.PurchaseInvoices.Add(Purchase("COM", items: [CleanPurchaseLine()]));
        await Save(db);

        var issue = Assert.Single(await IssuesOf(db, "Item sem CST de ICMS"));

        Assert.Equal("SEM", issue.InternalNumber);
    }

    [Fact]
    public async Task Build_IssueCfopDirectionMismatch()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.SalesInvoices.Add(CleanSale("SAIDA", cfop: "1202"));
        db.Context.PurchaseInvoices.Add(Purchase("ENTRADA", items: [CleanPurchaseLine("5101")]));
        db.Context.SalesInvoices.Add(CleanSale("OK", cfop: "5101"));
        await Save(db);

        var data = await Service(db).BuildAsync(Request());

        var incoming = Assert.Single(data.Issues, i => i.Rule == "CFOP de entrada em documento de saída");
        var outgoing = Assert.Single(data.Issues, i => i.Rule == "CFOP de saída em documento de entrada");
        Assert.Equal("SAIDA", incoming.InternalNumber);
        Assert.Equal("ENTRADA", outgoing.InternalNumber);
    }

    [Fact]
    public async Task Build_ReturnWithOppositeCfopIsNotADirectionIssue()
    {
        var db = TestDb.CreateUnitOfWork();
        var saleReturn = CleanSale("DEVV", cfop: "1202");
        saleReturn.InvoiceType = SalesInvoiceType.Return;
        db.Context.SalesInvoices.Add(saleReturn);
        db.Context.PurchaseInvoices.Add(Purchase("DEVC", type: PurchaseInvoiceType.Return, items: [CleanPurchaseLine("5202")]));
        await Save(db);

        var data = await Service(db).BuildAsync(Request());

        Assert.DoesNotContain(data.Issues, i => i.Rule.StartsWith("CFOP de "));
    }

    [Fact]
    public async Task Build_IssueIcmsMismatch()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.SalesInvoices.Add(OwnSale("ERRO", icms: Icms("00", 12m, 1000m, 100m)));
        db.Context.SalesInvoices.Add(OwnSale("CERTO", icms: Icms("00", 12m, 1000m, 120m)));
        await Save(db);

        var issue = Assert.Single(await IssuesOf(db, "ICMS difere de base × alíquota"));

        Assert.Equal("ERRO", issue.InternalNumber);
    }

    [Fact]
    public async Task Build_IcmsDifferenceOfOneCentIsNotAnIssue()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.SalesInvoices.Add(OwnSale("UM", icms: Icms("00", 12m, 1000m, 120.01m)));
        db.Context.SalesInvoices.Add(OwnSale("DOIS", icms: Icms("00", 12m, 1000m, 120.02m)));
        await Save(db);

        var issue = Assert.Single(await IssuesOf(db, "ICMS difere de base × alíquota"));

        Assert.Equal("DOIS", issue.InternalNumber);
    }

    [Fact]
    public async Task Build_NfeWithoutAccessKey()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.SalesInvoices.Add(CleanSale("OK"));
        db.Context.SalesInvoices.Add(CleanSale("CURTA").With(s => s.ChaveNFe = "123"));
        db.Context.SalesInvoices.Add(CleanSale("LETRA").With(s => s.ChaveNFe = new string('A', 44)));
        db.Context.SalesInvoices.Add(CleanSale("NULA").With(s => s.ChaveNFe = null));
        await Save(db);

        var issues = await IssuesOf(db, "NF-e sem chave de acesso válida");

        Assert.Equal(new[] { "CURTA", "LETRA", "NULA" }, issues.Select(i => i.InternalNumber).Order());
    }

    [Fact]
    public async Task Build_OtherModelWithoutKeyIsNotAnIssue()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.SalesInvoices.Add(CleanSale("OUT").With(s => { s.TaxDocumentKind = TaxDocumentKind.Other; s.ChaveNFe = null; }));
        await Save(db);

        Assert.Empty(await IssuesOf(db, "NF-e sem chave de acesso válida"));
    }

    [Fact]
    public async Task Build_OwnNfeNotAuthorized()
    {
        var db = TestDb.CreateUnitOfWork();
        var two = OwnSale("NAO", nfe: NfeStatus.None);
        two.Items.Add(SaleItem("10002").With(i => i.SalesInvoiceKey = two.Key));
        db.Context.SalesInvoices.Add(two);
        db.Context.PurchaseInvoices.Add(Purchase("PROPRIA", issuer: DocumentIssuerType.Own, items: [CleanPurchaseLine("5202")]));
        db.Context.PurchaseInvoices.Add(Purchase("TERCEIRO", items: [CleanPurchaseLine()]));
        await Save(db);

        var issues = await IssuesOf(db, "NF-e própria confirmada e não autorizada");

        Assert.Equal(new[] { "NAO", "PROPRIA" }, issues.Select(i => i.InternalNumber).Order());
        Assert.All(issues, i => Assert.Null(i.ItemCode));
    }

    [Fact]
    public async Task Build_OwnNfeNotAuthorizedIsLeftOutOfTotals()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.SalesInvoices.Add(OwnSale("NAO", Icms("00", 12m, 1000m, 120m)).With(s => s.NfeStatus = NfeStatus.None));
        await Save(db);

        var data = await Service(db).BuildAsync(Request());

        Assert.Empty(data.Documents);
        Assert.Equal(0m, data.AssessmentTotals.Debits);
        Assert.Single(data.Issues);
    }

    [Fact]
    public async Task Build_IssuesAreOrderedByDateNumberItem()
    {
        var db = TestDb.CreateUnitOfWork();
        var late = CleanSale("A", cfop: null);
        late.InvoiceDate = new DateTime(2026, 7, 20);
        var b = CleanSale("B", cfop: null);
        var a = CleanSale("A", cfop: null);
        a.Items.Add(SaleItem("00001").With(i => { i.SalesInvoiceKey = a.Key; i.CstIcms = "00"; }));
        db.Context.SalesInvoices.AddRange(late, b, a);
        await Save(db);

        var issues = await IssuesOf(db, "Item sem CFOP");

        Assert.Equal(new[] { ("A", "00001"), ("A", "10001"), ("B", "10001"), ("A", "10001") },
            issues.Select(i => (i.Number, i.ItemCode!)));
        Assert.Equal(new DateTime(2026, 7, 20), issues[^1].Date);
    }

    private static (string Cst, decimal Rate, decimal Base, decimal Value) Icms(string cst, decimal rate, decimal @base, decimal value) =>
        (cst, rate, @base, value);

    private static SalesInvoice OwnSale(string number, (string Cst, decimal Rate, decimal Base, decimal Value) icms)
    {
        var sale = OwnSale(number);
        var item = sale.Items.First();
        item.CstIcms = icms.Cst; item.IcmsRate = icms.Rate; item.IcmsBase = icms.Base; item.IcmsValue = icms.Value;
        item.Quantity = 1m; item.UnitPrice = icms.Base;
        return sale;
    }

    private static PurchaseInvoiceItem PurchaseLine(string? cfop, (string Cst, decimal Rate, decimal Base, decimal Value) icms)
    {
        var item = PurchaseLine(cfop);
        item.CstIcms = icms.Cst; item.IcmsRate = icms.Rate; item.IcmsBase = icms.Base; item.IcmsValue = icms.Value;
        item.Quantity = 1m; item.UnitPrice = icms.Base;
        return item;
    }

    private static SalesInvoice OwnSale(
        string number,
        InvoiceStatus status = InvoiceStatus.Confirmed,
        NfeStatus nfe = NfeStatus.Authorized,
        string branch = "01",
        string? cfop = "5101")
    {
        var item = SaleItem();
        item.Cfop = cfop;
        var sale = Sale(number, status: status, branchCode: branch, items: item);
        sale.NfeStatus = nfe;
        sale.ChaveNFe = Key44;
        return sale;
    }

    private static PurchaseInvoiceItem PurchaseLine(string? cfop)
    {
        var item = PurchaseItem();
        item.Cfop = cfop;
        return item;
    }

    private static FiscalConferenceService Service(IUnitOfWork db) =>
        new(db, new FunruralWithholdingReportService(db, new RecordingFastReportService(), TestConfiguration.Erp("STANDALONE")));

    private static FiscalConferenceRequest Request() => new() { Year = 2026, Month = 7 };

    private static async Task Save(IUnitOfWork db)
    {
        await db.Context.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();
    }
}

internal static class FiscalConferenceTestExtensions
{
    public static T With<T>(this T entity, Action<T> change)
    {
        change(entity);
        return entity;
    }
}
