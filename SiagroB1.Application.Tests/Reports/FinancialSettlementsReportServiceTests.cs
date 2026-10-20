using Microsoft.Extensions.Configuration;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using SiagroB1.Infra.Queries;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Services;

namespace SiagroB1.Application.Tests.Reports;

public class FinancialSettlementsReportServiceTests : IDisposable
{
    private readonly string _contentRoot;

    public FinancialSettlementsReportServiceTests()
    {
        global::FastReport.Utils.RegisteredObjects.AddConnection(typeof(global::FastReport.Data.MsSqlDataConnection));
        global::FastReport.Utils.Config.WebMode = true;

        _contentRoot = Path.Combine(Path.GetTempPath(), "siagro-settlements-pdf", Guid.NewGuid().ToString("N"));
        var templates = Path.Combine(_contentRoot, "Reports", "Templates");
        Directory.CreateDirectory(templates);
        foreach (var file in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "ReportTemplates"), "*.frx"))
            File.Copy(file, Path.Combine(templates, Path.GetFileName(file)));

        var images = Path.Combine(_contentRoot, "wwwroot", "images");
        Directory.CreateDirectory(images);
        File.Copy(
            Path.Combine(AppContext.BaseDirectory, "ReportsContentRoot", "wwwroot", "images", "logo.png"),
            Path.Combine(images, "logo.png"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_contentRoot))
            Directory.Delete(_contentRoot, recursive: true);
        GC.SuppressFinalize(this);
    }

    private static FinancialDocument Doc(string code, decimal amount = 100m, string cardCode = "C1",
        FinancialDocumentNature nature = FinancialDocumentNature.Firm, FinancialDirection direction = FinancialDirection.Receivable) => new()
    {
        Key = Guid.NewGuid(), Code = code, CardCode = cardCode, CardName = "Parceiro " + cardCode,
        Nature = nature, OriginType = FinancialDocumentOrigin.Manual,
        DocumentDate = new DateTime(2026, 9, 1), DueDate = new DateTime(2026, 10, 30), NetAmount = amount,
        Direction = direction
    };

    private static FinancialSettlement Settle(FinancialDocument doc, decimal amount, DateTime date,
        FinancialSettlementOrigin origin = FinancialSettlementOrigin.Manual, string? account = null) => new()
    {
        Key = Guid.NewGuid(), FinancialDocumentKey = doc.Key, Amount = amount, SettlementDate = date, Origin = origin,
        FinancialAccountCode = account
    };

    private static FinancialSettlementsRequest Request() => new()
    {
        Direction = FinancialDirection.Receivable, DateFrom = new DateTime(2026, 10, 1),
        DateTo = new DateTime(2026, 10, 31), Mode = FinancialPositionMode.Firm
    };

    private static async Task<List<FinancialSettlementRowDto>> RowsAsync(IUnitOfWork db, FinancialSettlementsRequest? request = null)
    {
        var recorder = new RecordingFastReportService();
        await new FinancialSettlementsReportService(db, recorder).ExecuteAsync(request ?? Request());
        Assert.Equal("FinancialSettlements.frx", recorder.LastReportName);
        return Assert.IsAssignableFrom<ICollection<FinancialSettlementRowDto>>(recorder.LastData).ToList();
    }

    [Fact]
    public void Validate_requires_ordered_period()
    {
        Assert.Equal("Informe o período.", FinancialSettlementsReportService.Validate(new FinancialSettlementsRequest()));
        var reversed = Request();
        reversed.DateFrom = reversed.DateTo.AddDays(1);
        Assert.Equal("A data inicial deve ser menor ou igual à final.", FinancialSettlementsReportService.Validate(reversed));
        Assert.Null(FinancialSettlementsReportService.Validate(Request()));
    }

    [Fact]
    public async Task Filters_by_settlement_date()
    {
        var db = TestDb.CreateUnitOfWork();
        var doc = Doc("D1", 300m);
        db.Context.FinancialDocuments.Add(doc);
        db.Context.FinancialSettlements.AddRange(
            Settle(doc, 50m, new DateTime(2026, 9, 30)), Settle(doc, 70m, new DateTime(2026, 10, 1)),
            Settle(doc, 80m, new DateTime(2026, 10, 31)), Settle(doc, 90m, new DateTime(2026, 11, 1)));
        await db.Context.SaveChangesAsync();

        var rows = await RowsAsync(db);

        Assert.Equal(new[] { 70m, 80m }, rows.Select(r => r.Amount).ToArray());
        Assert.Equal(new[] { "01/10/2026", "31/10/2026" }, rows.Select(r => r.Date).ToArray());
    }

    [Fact]
    public async Task Distributed_slice_shows_on_firm_with_provisional_account()
    {
        var db = TestDb.CreateUnitOfWork();
        var provisional = Doc("PROV", 300m, "C9", FinancialDocumentNature.Provisional, FinancialDirection.Payable);
        var firm = Doc("FIRM", 100m, "C1", direction: FinancialDirection.Payable);
        db.Context.FinancialDocuments.AddRange(provisional, firm);
        db.Context.FinancialSettlements.AddRange(
            new FinancialSettlement
            {
                Key = Guid.NewGuid(), FinancialDocumentKey = firm.Key, Amount = 100m, SettlementDate = new DateTime(2026, 9, 2),
                Origin = FinancialSettlementOrigin.InvoiceOffset, CompositionKey = Guid.NewGuid(), RelatedDocumentKey = provisional.Key
            },
            Settle(provisional, 60m, new DateTime(2026, 10, 5), account: "CX1"));
        await db.Context.SaveChangesAsync();
        var request = Request();
        request.Direction = FinancialDirection.Payable;
        request.CardCode = "C1";

        var rows = await RowsAsync(db, request);

        var row = Assert.Single(rows);
        Assert.Equal(("FIRM", "CX1", "Baixa", 60m), (row.Document, row.Account, row.Method, row.Amount));
    }

    private static FinancialSettlement Offset(FinancialDocument composed, FinancialDocument provisional, decimal amount) => new()
    {
        Key = Guid.NewGuid(), FinancialDocumentKey = composed.Key, Amount = amount, SettlementDate = new DateTime(2026, 9, 2),
        Origin = FinancialSettlementOrigin.InvoiceOffset, CompositionKey = Guid.NewGuid(), RelatedDocumentKey = provisional.Key
    };

    [Fact]
    public async Task Distributed_slice_without_partner_filter_shows_firm_whose_own_lines_are_earlier()
    {
        var db = TestDb.CreateUnitOfWork();
        var provisional = Doc("PROV", 300m, "C9", FinancialDocumentNature.Provisional, FinancialDirection.Payable);
        var firm = Doc("FIRM", 100m, "C1", direction: FinancialDirection.Payable);
        db.Context.FinancialDocuments.AddRange(provisional, firm);
        db.Context.FinancialSettlements.AddRange(Offset(firm, provisional, 100m),
            Settle(provisional, 60m, new DateTime(2026, 10, 5), account: "CX1"));
        await db.Context.SaveChangesAsync();
        var request = Request();
        request.Direction = FinancialDirection.Payable;

        var rows = await RowsAsync(db, request);

        var row = Assert.Single(rows);
        Assert.Equal(("FIRM", "CX1", "Baixa", 60m), (row.Document, row.Account, row.Method, row.Amount));
    }

    [Fact]
    public async Task Provisional_mode_without_partner_filter_shows_payment_on_provisional()
    {
        var db = TestDb.CreateUnitOfWork();
        var provisional = Doc("PROV", 300m, "C9", FinancialDocumentNature.Provisional, FinancialDirection.Payable);
        var firm = Doc("FIRM", 100m, "C1", direction: FinancialDirection.Payable);
        db.Context.FinancialDocuments.AddRange(provisional, firm);
        db.Context.FinancialSettlements.AddRange(Offset(firm, provisional, 100m),
            Settle(provisional, 60m, new DateTime(2026, 10, 5), account: "CX1"));
        await db.Context.SaveChangesAsync();
        var request = Request();
        request.Direction = FinancialDirection.Payable;
        request.Mode = FinancialPositionMode.Provisional;

        var rows = await RowsAsync(db, request);

        var row = Assert.Single(rows);
        Assert.Equal(("PROV", "CX1", "Baixa", 60m), (row.Document, row.Account, row.Method, row.Amount));
    }

    [Fact]
    public async Task Return_netting_has_own_label_no_account_and_is_excluded_by_account_filter()
    {
        var db = TestDb.CreateUnitOfWork();
        var provisional = Doc("PROV", 300m, "C9", FinancialDocumentNature.Provisional, FinancialDirection.Payable);
        var firm = Doc("FIRM", 100m, "C1", direction: FinancialDirection.Payable);
        var ret = Doc("RET", 30m, "C1", FinancialDocumentNature.PurchaseReturn, FinancialDirection.Payable);
        db.Context.FinancialDocuments.AddRange(provisional, firm, ret);
        db.Context.FinancialSettlements.AddRange(Offset(firm, provisional, 100m), Offset(ret, provisional, 30m),
            Settle(provisional, 70m, new DateTime(2026, 10, 5), account: "CX1"));
        await db.Context.SaveChangesAsync();
        var request = Request();
        request.Direction = FinancialDirection.Payable;

        var all = await RowsAsync(db, request);

        var netting = all.Where(r => r.Method == "Abatimento de devolução").ToList();
        // Pool 70 = líquido (100 − 30): a devolução abate 30 do firme; as duas pernas saem com 30.
        Assert.Equal(2, netting.Count);
        Assert.Equal(30m, netting.Single(r => r.Document == "FIRM").Amount);
        Assert.Equal(30m, netting.Single(r => r.Document == "RET").Amount);
        Assert.All(netting, r => Assert.Equal(string.Empty, r.Account));
        Assert.Contains(all, r => r.Method == "Baixa" && r.Account == "CX1" && r.Amount == 70m);

        request.AccountCode = "CX1";
        var filtered = await RowsAsync(db, request);

        Assert.DoesNotContain(filtered, r => r.Method == "Abatimento de devolução");
        Assert.Contains(filtered, r => r.Method == "Baixa" && r.Amount == 70m);
    }

    [Fact]
    public async Task Reversal_shows_negative()
    {
        var db = TestDb.CreateUnitOfWork();
        var doc = Doc("D1");
        db.Context.FinancialDocuments.Add(doc);
        db.Context.FinancialSettlements.AddRange(Settle(doc, 100m, new DateTime(2026, 10, 5), account: "CX1"),
            Settle(doc, -100m, new DateTime(2026, 10, 6), FinancialSettlementOrigin.Reversal, "CX1"));
        await db.Context.SaveChangesAsync();

        var rows = await RowsAsync(db);

        Assert.Equal(new[] { 100m, -100m }, rows.Select(r => r.Amount).ToArray());
        Assert.Equal("Estorno", rows[1].Method);
    }

    [Fact]
    public async Task Advance_application_line_is_not_listed_on_the_advance()
    {
        var db = TestDb.CreateUnitOfWork();
        var advance = Doc("AD1", 150m, nature: FinancialDocumentNature.Advance);
        db.Context.FinancialDocuments.Add(advance);
        db.Context.FinancialSettlements.AddRange(Settle(advance, 150m, new DateTime(2026, 10, 1), account: "CX1"),
            Settle(advance, 150m, new DateTime(2026, 10, 5), FinancialSettlementOrigin.AdvanceApplication));
        await db.Context.SaveChangesAsync();

        var rows = await RowsAsync(db);

        var row = Assert.Single(rows);
        Assert.Equal(150m, row.Amount);
        Assert.Equal("Baixa", row.Method);
    }

    [Theory]
    [InlineData(FinancialSettlementOrigin.Manual, "Baixa")]
    [InlineData(FinancialSettlementOrigin.Reversal, "Estorno")]
    [InlineData(FinancialSettlementOrigin.InvoiceOffset, "Abatimento")]
    [InlineData(FinancialSettlementOrigin.AdvanceApplication, "Aplicação de adiantamento")]
    [InlineData(FinancialSettlementOrigin.Netting, "Encontro de contas")]
    [InlineData(FinancialSettlementOrigin.AdvanceRefund, "Devolução de adiantamento")]
    [InlineData(FinancialSettlementOrigin.Dation, "Dação")]
    [InlineData(FinancialSettlementOrigin.AdvanceTransfer, "Transferência de adiantamento")]
    public async Task Origin_labels_pt_BR(FinancialSettlementOrigin origin, string label)
    {
        var db = TestDb.CreateUnitOfWork();
        var doc = Doc("D1");
        db.Context.FinancialDocuments.Add(doc);
        db.Context.FinancialSettlements.Add(Settle(doc, 10m, new DateTime(2026, 10, 5), origin));
        await db.Context.SaveChangesAsync();

        var rows = await RowsAsync(db);

        Assert.Equal(label, Assert.Single(rows).Method);
    }

    [Fact]
    public async Task Filters_by_account()
    {
        var db = TestDb.CreateUnitOfWork();
        var invoice = new SalesInvoice { Key = Guid.NewGuid(), CardCode = "C1", TaxDocumentNumber = "000123456", TaxDocumentSeries = "001" };
        db.Context.SalesInvoices.Add(invoice);
        var doc = Doc("D1", 300m);
        doc.SalesInvoiceKey = invoice.Key;
        db.Context.FinancialDocuments.Add(doc);
        db.Context.FinancialSettlements.AddRange(Settle(doc, 10m, new DateTime(2026, 10, 5), account: "CX1"),
            Settle(doc, 20m, new DateTime(2026, 10, 5), account: "CX2"));
        await db.Context.SaveChangesAsync();
        var request = Request();
        request.AccountCode = "CX2";

        var rows = await RowsAsync(db, request);

        var row = Assert.Single(rows);
        Assert.Equal((20m, "CX2"), (row.Amount, row.Account));
    }

    [Fact]
    public async Task Totals_are_per_currency()
    {
        var db = TestDb.CreateUnitOfWork();
        var doc = Doc("D1", 300m);
        var usd = Doc("D2", 300m, "C2");
        usd.Currency = CurrencyType.Usd;
        db.Context.FinancialDocuments.AddRange(doc, usd);
        db.Context.FinancialSettlements.AddRange(Settle(doc, 10m, new DateTime(2026, 10, 5)), Settle(usd, 5m, new DateTime(2026, 10, 5)));
        await db.Context.SaveChangesAsync();
        var recorder = new RecordingFastReportService();

        await new FinancialSettlementsReportService(db, recorder).ExecuteAsync(Request());

        var lines = ((string)recorder.LastParameters!["pTotals"]).Split('\n');
        Assert.Equal(2, lines.Length);
        Assert.Contains("BRL: valor 10,00", lines[0]);
        Assert.Contains("USD: valor 5,00", lines[1]);
        Assert.Equal("Baixas de Recebíveis", recorder.LastParameters["pTitle"]);
        Assert.Equal("Baixas de 01/10/2026 a 31/10/2026", recorder.LastParameters["pPeriod"]);
    }

    [Fact]
    public async Task Rows_carry_the_invoice_number_and_series_of_the_title()
    {
        var db = TestDb.CreateUnitOfWork();
        var invoice = new PurchaseInvoice { Key = Guid.NewGuid(), CardCode = "C1", TaxDocumentNumber = "8001", TaxDocumentSeries = "1" };
        db.Context.PurchaseInvoices.Add(invoice);
        var firm = Doc("NF", 100m);
        firm.PurchaseInvoiceKey = invoice.Key;
        var manual = Doc("MAN", 100m);
        db.Context.FinancialDocuments.AddRange(firm, manual);
        db.Context.FinancialSettlements.AddRange(Settle(firm, 10m, new DateTime(2026, 10, 5)), Settle(manual, 5m, new DateTime(2026, 10, 5)));
        await db.Context.SaveChangesAsync();

        var rows = await RowsAsync(db);

        var nf = rows.Single(r => r.Document == "NF");
        Assert.Equal(("8001", "1"), (nf.NfeNumber, nf.NfeSeries));
        var man = rows.Single(r => r.Document == "MAN");
        Assert.Equal(("", ""), (man.NfeNumber, man.NfeSeries));
    }

    [Fact]
    public async Task Distributed_slice_shows_the_invoice_of_the_composed_firm()
    {
        var db = TestDb.CreateUnitOfWork();
        var invoice = new PurchaseInvoice { Key = Guid.NewGuid(), CardCode = "C1", TaxDocumentNumber = "8001", TaxDocumentSeries = "1" };
        db.Context.PurchaseInvoices.Add(invoice);
        var provisional = Doc("PROV", 300m, "C9", FinancialDocumentNature.Provisional, FinancialDirection.Payable);
        var firm = Doc("FIRM", 100m, "C1", direction: FinancialDirection.Payable);
        firm.PurchaseInvoiceKey = invoice.Key;
        db.Context.FinancialDocuments.AddRange(provisional, firm);
        db.Context.FinancialSettlements.AddRange(Offset(firm, provisional, 100m),
            Settle(provisional, 60m, new DateTime(2026, 10, 5), account: "CX1"));
        await db.Context.SaveChangesAsync();
        var request = Request();
        request.Direction = FinancialDirection.Payable;

        var row = Assert.Single(await RowsAsync(db, request));

        Assert.Equal(("FIRM", "8001", "1"), (row.Document, row.NfeNumber, row.NfeSeries));
    }

    [Fact]
    public async Task Generates_pdf()
    {
        var db = TestDb.CreateUnitOfWork();
        var invoice = new SalesInvoice { Key = Guid.NewGuid(), CardCode = "C1", TaxDocumentNumber = "000123456", TaxDocumentSeries = "001" };
        db.Context.SalesInvoices.Add(invoice);
        var doc = Doc("D1", 300m);
        doc.SalesInvoiceKey = invoice.Key;
        db.Context.FinancialDocuments.Add(doc);
        db.Context.FinancialSettlements.AddRange(Settle(doc, 10m, new DateTime(2026, 10, 5), account: "CX1"),
            Settle(doc, 20m, new DateTime(2026, 10, 6)));
        await db.Context.SaveChangesAsync();

        var pdf = await new FinancialSettlementsReportService(db, FastReport()).ExecuteAsync(Request());

        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(pdf, 0, 4));
        // Amostra só sob demanda (SIAGRO_WRITE_SAMPLE_PDF=1): o teste não deixa lixo no %TEMP%.
        ReportSample.WriteIfRequested("siagro-settlements-reports", "FinancialSettlements.pdf", pdf);
    }

    private FastReportService FastReport()
    {
        var env = new TestWebHostEnvironment(_contentRoot);
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Erp"] = "STANDALONE",
                ["CompanyName"] = "ACME AGRO LTDA",
                ["CompanyLogoPath"] = "wwwroot/images/logo.png",
            })
            .Build();
        return new FastReportService(env, configuration,
            new ReportHeaderService(env, configuration, new TestLogger<ReportHeaderService>(), FakeCompanyContext.Unresolved()),
            FakeCompanyContext.Unresolved());
    }

    [Fact]
    public async Task Return_settlement_shows_recorded_positive_sign()
    {
        var db = TestDb.CreateUnitOfWork();
        var ret = Doc("DEV", 30m, nature: FinancialDocumentNature.SalesReturn);
        db.Context.FinancialDocuments.Add(ret);
        db.Context.FinancialSettlements.Add(Settle(ret, 30m, new DateTime(2026, 10, 5), FinancialSettlementOrigin.Netting));
        await db.Context.SaveChangesAsync();

        var row = Assert.Single(await RowsAsync(db));

        Assert.Equal(("Encontro de contas", 30m), (row.Method, row.Amount));
    }

    [Fact]
    public async Task Return_netting_slice_on_composed_return_shows_positive()
    {
        var db = TestDb.CreateUnitOfWork();
        var provisional = Doc("PROV", 300m, "C9", FinancialDocumentNature.Provisional, FinancialDirection.Payable);
        var firm = Doc("FIRM", 100m, "C1", direction: FinancialDirection.Payable);
        var ret = Doc("RET", 30m, "C1", FinancialDocumentNature.PurchaseReturn, FinancialDirection.Payable);
        db.Context.FinancialDocuments.AddRange(provisional, firm, ret);
        db.Context.FinancialSettlements.AddRange(Offset(firm, provisional, 100m), Offset(ret, provisional, 30m),
            Settle(provisional, 70m, new DateTime(2026, 10, 5), account: "CX1"));
        await db.Context.SaveChangesAsync();
        var request = Request();
        request.Direction = FinancialDirection.Payable;

        var rows = await RowsAsync(db, request);

        Assert.Equal(30m, Assert.Single(rows, r => r.Document == "RET").Amount);
    }
}
