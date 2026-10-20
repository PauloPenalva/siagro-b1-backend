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
