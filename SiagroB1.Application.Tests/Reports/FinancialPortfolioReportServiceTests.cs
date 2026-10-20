using Microsoft.Extensions.Configuration;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using SiagroB1.Infra.Queries;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Services;

namespace SiagroB1.Application.Tests.Reports;

public class FinancialPortfolioReportServiceTests : IDisposable
{
    private readonly string _contentRoot;

    public FinancialPortfolioReportServiceTests()
    {
        global::FastReport.Utils.RegisteredObjects.AddConnection(typeof(global::FastReport.Data.MsSqlDataConnection));
        global::FastReport.Utils.Config.WebMode = true;

        _contentRoot = Path.Combine(Path.GetTempPath(), "siagro-portfolio-pdf", Guid.NewGuid().ToString("N"));
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

    private static FinancialDocument Doc(string code, DateTime due, decimal amount = 100m,
        CurrencyType currency = CurrencyType.Brl, string cardCode = "C1", string? origin = null) => new()
    {
        Key = Guid.NewGuid(), Code = code, CardCode = cardCode, CardName = "Parceiro " + cardCode,
        Nature = FinancialDocumentNature.Firm, OriginType = FinancialDocumentOrigin.Manual,
        DocumentDate = new DateTime(2026, 9, 1), DueDate = due, NetAmount = amount, Currency = currency,
        Direction = FinancialDirection.Receivable, OriginDocNumber = origin
    };

    private static FinancialPortfolioRequest Request() => new()
    {
        Direction = FinancialDirection.Receivable, DueDateFrom = new DateTime(2026, 10, 1),
        DueDateTo = new DateTime(2026, 10, 31), Mode = FinancialPositionMode.Firm
    };

    private static async Task<List<FinancialPortfolioRowDto>> RowsAsync(IUnitOfWork db, RecordingFastReportService recorder,
        FinancialPortfolioRequest? request = null)
    {
        await new FinancialPortfolioReportService(db, recorder).ExecuteAsync(request ?? Request());
        Assert.Equal("FinancialPortfolio.frx", recorder.LastReportName);
        return Assert.IsAssignableFrom<ICollection<FinancialPortfolioRowDto>>(recorder.LastData).ToList();
    }

    [Fact]
    public void Validate_requires_ordered_period()
    {
        Assert.Equal("Informe o período.", FinancialPortfolioReportService.Validate(new FinancialPortfolioRequest()));
        var reversed = Request();
        reversed.DueDateFrom = reversed.DueDateTo.AddDays(1);
        Assert.Equal("A data inicial deve ser menor ou igual à final.", FinancialPortfolioReportService.Validate(reversed));
        Assert.Null(FinancialPortfolioReportService.Validate(Request()));
    }

    [Fact]
    public async Task Filters_by_due_date_and_open_amount()
    {
        var db = TestDb.CreateUnitOfWork();
        var paid = Doc("PAID", new DateTime(2026, 10, 5));
        db.Context.FinancialDocuments.AddRange(Doc("IN", new DateTime(2026, 10, 5), origin: "CT-1"),
            Doc("OUT", new DateTime(2026, 11, 5)), paid);
        db.Context.FinancialSettlements.Add(new FinancialSettlement
        {
            Key = Guid.NewGuid(), FinancialDocumentKey = paid.Key, Amount = 100m, SettlementDate = new DateTime(2026, 10, 5)
        });
        await db.Context.SaveChangesAsync();

        var rows = await RowsAsync(db, new RecordingFastReportService());

        var row = Assert.Single(rows);
        Assert.Equal(("IN", "CT-1", "BRL", 100m, 0m, 100m), (row.Document, row.Origin, row.Currency, row.Amount, row.Settled, row.Open));
        Assert.Equal("Parceiro C1", row.Partner);
    }

    [Fact]
    public async Task Days_overdue_is_base_minus_due_when_positive()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.FinancialDocuments.AddRange(Doc("LATE", new DateTime(2026, 10, 21)), Doc("LAST", new DateTime(2026, 10, 31)));
        await db.Context.SaveChangesAsync();

        var rows = await RowsAsync(db, new RecordingFastReportService());

        Assert.Equal(10, rows.Single(r => r.Document == "LATE").DaysOverdue);
        Assert.Equal(0, rows.Single(r => r.Document == "LAST").DaysOverdue);
        Assert.Equal("Manual", rows[0].Origin);
    }

    [Fact]
    public async Task Totals_are_per_currency()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.FinancialDocuments.AddRange(Doc("A", new DateTime(2026, 10, 5), 100m),
            Doc("B", new DateTime(2026, 10, 6), 50m, cardCode: "C2"),
            Doc("U", new DateTime(2026, 10, 7), 30m, CurrencyType.Usd));
        await db.Context.SaveChangesAsync();
        var recorder = new RecordingFastReportService();

        await RowsAsync(db, recorder);

        var lines = ((string)recorder.LastParameters!["pTotals"]).Split('\n');
        Assert.Equal(2, lines.Length);
        Assert.Contains("BRL", lines[0]);
        Assert.Contains("150,00", lines[0]);
        Assert.Contains("USD", lines[1]);
        Assert.Contains("30,00", lines[1]);
        Assert.Equal("Contas a Receber", recorder.LastParameters["pTitle"]);
        Assert.Equal("Vencimento: 01/10/2026 a 31/10/2026 · Posição em 31/10/2026 (data-base)", recorder.LastParameters["pPeriod"]);
        Assert.Equal("Títulos: Firmes", recorder.LastParameters["pMode"]);
    }

    [Fact]
    public async Task Rows_carry_the_invoice_number_and_series_of_the_title()
    {
        var db = TestDb.CreateUnitOfWork();
        var invoice = new SalesInvoice { Key = Guid.NewGuid(), CardCode = "C1", TaxDocumentNumber = "293", TaxDocumentSeries = "3" };
        db.Context.SalesInvoices.Add(invoice);
        var firm = Doc("NF", new DateTime(2026, 10, 5));
        firm.OriginType = FinancialDocumentOrigin.SalesInvoice;
        firm.SalesInvoiceKey = invoice.Key;
        db.Context.FinancialDocuments.AddRange(firm, Doc("MAN", new DateTime(2026, 10, 6)));
        await db.Context.SaveChangesAsync();

        var rows = await RowsAsync(db, new RecordingFastReportService());

        var nf = rows.Single(r => r.Document == "NF");
        Assert.Equal(("293", "3"), (nf.NfeNumber, nf.NfeSeries));
        var man = rows.Single(r => r.Document == "MAN");
        Assert.Equal(("", ""), (man.NfeNumber, man.NfeSeries));
    }

    [Fact]
    public async Task Generates_pdf()
    {
        var db = TestDb.CreateUnitOfWork();
        var invoice = new SalesInvoice { Key = Guid.NewGuid(), CardCode = "C1", TaxDocumentNumber = "000123456", TaxDocumentSeries = "001" };
        db.Context.SalesInvoices.Add(invoice);
        var withInvoice = Doc("A", new DateTime(2026, 10, 5), 100m, origin: "CT-2026-0001");
        withInvoice.SalesInvoiceKey = invoice.Key;
        db.Context.FinancialDocuments.AddRange(withInvoice,
            Doc("B", new DateTime(2026, 10, 6), 50m, cardCode: "C2"),
            Doc("U", new DateTime(2026, 10, 7), 30m, CurrencyType.Usd));
        await db.Context.SaveChangesAsync();

        var pdf = await new FinancialPortfolioReportService(db, FastReport()).ExecuteAsync(Request());

        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(pdf, 0, 4));
        // Amostra só sob demanda (SIAGRO_WRITE_SAMPLE_PDF=1): o teste não deixa lixo no %TEMP%.
        ReportSample.WriteIfRequested("siagro-portfolio-reports", "FinancialPortfolio.pdf", pdf);
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
}
