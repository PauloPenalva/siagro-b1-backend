using Microsoft.Extensions.Configuration;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using SiagroB1.Infra.Queries;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Services;

namespace SiagroB1.Application.Tests.Reports;

public class PartnerStatementReportServiceTests : IDisposable
{
    private readonly string _contentRoot;

    public PartnerStatementReportServiceTests()
    {
        global::FastReport.Utils.RegisteredObjects.AddConnection(typeof(global::FastReport.Data.MsSqlDataConnection));
        global::FastReport.Utils.Config.WebMode = true;

        _contentRoot = Path.Combine(Path.GetTempPath(), "siagro-partner-statement-pdf", Guid.NewGuid().ToString("N"));
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

    private static readonly DateTime From = new(2026, 10, 1);
    private static readonly DateTime To = new(2026, 10, 31);

    private static FinancialDocument Doc(string code, decimal amount = 100m, string cardCode = "C1",
        FinancialDocumentNature nature = FinancialDocumentNature.Firm, FinancialDirection direction = FinancialDirection.Receivable,
        DateTime? date = null, FinancialDocumentOrigin origin = FinancialDocumentOrigin.Manual) => new()
    {
        Key = Guid.NewGuid(), Code = code, CardCode = cardCode, CardName = "Parceiro " + cardCode,
        Nature = nature, OriginType = origin,
        DocumentDate = date ?? new DateTime(2026, 10, 10), DueDate = new DateTime(2026, 11, 30), NetAmount = amount,
        Direction = direction
    };

    private static FinancialSettlement Settle(FinancialDocument doc, decimal amount, DateTime date,
        FinancialSettlementOrigin origin = FinancialSettlementOrigin.Manual) => new()
    {
        Key = Guid.NewGuid(), FinancialDocumentKey = doc.Key, Amount = amount, SettlementDate = date, Origin = origin
    };

    private static FinancialSettlement Offset(FinancialDocument composed, FinancialDocument provisional, decimal amount, DateTime date,
        FinancialSettlementOrigin origin = FinancialSettlementOrigin.InvoiceOffset) => new()
    {
        Key = Guid.NewGuid(), FinancialDocumentKey = composed.Key, Amount = amount, SettlementDate = date,
        Origin = origin, CompositionKey = Guid.NewGuid(), RelatedDocumentKey = provisional.Key
    };

    private static PartnerStatementRequest Request(FinancialDirection? direction = FinancialDirection.Receivable,
        FinancialPositionMode mode = FinancialPositionMode.Firm) => new()
    {
        CardCode = "C1", DateFrom = From, DateTo = To, Direction = direction, Mode = mode
    };

    private static async Task<(List<PartnerStatementRowDto> Rows, Dictionary<string, object> Parameters)> RunAsync(
        IUnitOfWork db, PartnerStatementRequest? request = null)
    {
        var recorder = new RecordingFastReportService();
        await new PartnerStatementReportService(db, recorder).ExecuteAsync(request ?? Request());
        Assert.Equal("PartnerStatement.frx", recorder.LastReportName);
        var rows = Assert.IsAssignableFrom<ICollection<PartnerStatementRowDto>>(recorder.LastData).ToList();
        return (rows, recorder.LastParameters!);
    }

    private static async Task<IUnitOfWork> SeedAsync(IEnumerable<FinancialDocument> docs, params FinancialSettlement[] settlements)
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.FinancialDocuments.AddRange(docs);
        db.Context.FinancialSettlements.AddRange(settlements);
        await db.Context.SaveChangesAsync();
        return db;
    }

    [Fact]
    public void Validate_requires_partner_and_ordered_period()
    {
        var noPartner = Request();
        noPartner.CardCode = " ";
        Assert.Equal("Informe o parceiro.", PartnerStatementReportService.Validate(noPartner));
        var noPeriod = Request();
        noPeriod.DateFrom = default;
        Assert.Equal("Informe o período.", PartnerStatementReportService.Validate(noPeriod));
        var reversed = Request();
        reversed.DateFrom = reversed.DateTo.AddDays(1);
        Assert.Equal("A data inicial deve ser menor ou igual à final.", PartnerStatementReportService.Validate(reversed));
        Assert.Null(PartnerStatementReportService.Validate(Request()));
    }

    [Fact]
    public async Task Opening_balance_is_position_on_day_before()
    {
        var old = Doc("OLD", 100m, date: new DateTime(2026, 9, 1));
        var edge = Doc("EDGE", 50m, date: From);
        var db = await SeedAsync([old, edge], Settle(old, 30m, new DateTime(2026, 9, 30)), Settle(old, 20m, From));

        var (rows, _) = await RunAsync(db);

        Assert.All(rows, r => Assert.Equal(70m, r.OpeningBalance));
        Assert.Equal(new[] { "Emissão EDGE", "Baixa OLD" }, rows.Select(r => r.History).ToArray());
        Assert.Equal(new[] { 120m, 100m }, rows.Select(r => r.Balance).ToArray());
    }

    [Fact]
    public async Task Receivable_issue_is_debit_settlement_is_credit()
    {
        var doc = Doc("D1", 100m);
        var db = await SeedAsync([doc], Settle(doc, 40m, new DateTime(2026, 10, 10)));

        var (rows, _) = await RunAsync(db);

        Assert.Equal(new[] { ("Emissão D1", 100m, 0m, 100m), ("Baixa D1", 0m, 40m, 60m) },
            rows.Select(r => (r.History, r.Debit, r.Credit, r.Balance)).ToArray());
        Assert.All(rows, r => Assert.Equal(("D1", "10/10/2026"), (r.Document, r.Date)));
    }

    [Fact]
    public async Task Payable_issue_is_credit_settlement_is_debit()
    {
        var doc = Doc("P1", 100m, direction: FinancialDirection.Payable);
        var db = await SeedAsync([doc], Settle(doc, 40m, new DateTime(2026, 10, 12)),
            Settle(doc, -40m, new DateTime(2026, 10, 13), FinancialSettlementOrigin.Reversal));

        var (rows, _) = await RunAsync(db, Request(FinancialDirection.Payable));

        Assert.Equal(new[] { ("Emissão P1", 0m, 100m, -100m), ("Baixa P1", 40m, 0m, -60m), ("Estorno P1", -40m, 0m, -100m) },
            rows.Select(r => (r.History, r.Debit, r.Credit, r.Balance)).ToArray());
    }

    [Fact]
    public async Task Both_directions_balance_sign()
    {
        var rec = Doc("R1", 100m);
        var pay = Doc("P1", 30m, direction: FinancialDirection.Payable);
        var other = Doc("X1", 999m, "C2");
        var db = await SeedAsync([rec, pay, other]);

        var (rows, parameters) = await RunAsync(db, Request(direction: null));

        Assert.Equal(new[] { "P1", "R1" }, rows.Select(r => r.Document).ToArray());
        Assert.Equal(70m, rows[^1].Balance);
        Assert.All(rows, r => Assert.Equal(70m, r.ClosingBalance));
        Assert.Contains("Ambos", (string)parameters["pDirection"]);
    }

    [Fact]
    public async Task Return_goes_to_opposite_column()
    {
        var ret = Doc("DEV", 30m, nature: FinancialDocumentNature.SalesReturn);
        var pay = Doc("PDEV", 20m, nature: FinancialDocumentNature.PurchaseReturn, direction: FinancialDirection.Payable);
        var db = await SeedAsync([ret, pay]);

        var (rows, _) = await RunAsync(db, Request(direction: null));

        Assert.Equal(new[] { ("Emissão DEV", 0m, 30m, -30m), ("Emissão PDEV", 20m, 0m, -10m) },
            rows.Select(r => (r.History, r.Debit, r.Credit, r.Balance)).ToArray());
    }

    [Fact]
    public async Task Order_is_date_then_issues_before_settlements_then_code()
    {
        var b = Doc("B", 10m, date: new DateTime(2026, 10, 5));
        var a = Doc("A", 10m, date: new DateTime(2026, 10, 5));
        var c = Doc("C", 10m, date: new DateTime(2026, 10, 2));
        var db = await SeedAsync([b, a, c], Settle(c, 5m, new DateTime(2026, 10, 5)));

        var (rows, _) = await RunAsync(db);

        Assert.Equal(new[] { "Emissão C", "Emissão A", "Emissão B", "Baixa C" }, rows.Select(r => r.History).ToArray());
    }

    private static void AssertInvariant(List<PartnerStatementRowDto> rows, decimal expectedOpening, decimal expectedClosing)
    {
        Assert.NotEmpty(rows);
        Assert.All(rows, r => Assert.Equal((expectedOpening, expectedClosing), (r.OpeningBalance, r.ClosingBalance)));
        Assert.Equal(expectedClosing, expectedOpening + rows.Sum(r => r.Debit - r.Credit));
        Assert.Equal(expectedClosing, rows[^1].Balance);
    }

    private static async Task<decimal> PositionAsync(IUnitOfWork db, DateTime baseDate, FinancialPositionMode mode,
        FinancialDirection direction)
    {
        var position = await FinancialPositionQuery.LoadAsync(db.Context, new FinancialPositionFilter(direction, "C1", baseDate),
            mode, CancellationToken.None);
        var sign = direction == FinancialDirection.Receivable ? 1m : -1m;
        return sign * position.Titles.Where(t => t.Document.CardCode == "C1" && t.Document.Direction == direction)
            .Sum(t => t.OpenAtBase);
    }

    [Fact]
    public async Task Closing_equals_opening_plus_movements_equals_position_at_end()
    {
        // R5 + R7: provisório (de outro parceiro) pago ANTES do período; firme composto DENTRO do período recebe dinheiro antigo.
        var provisional = Doc("PROV", 500m, "C9", FinancialDocumentNature.Provisional, FinancialDirection.Payable, new DateTime(2026, 9, 1));
        var early = Doc("F0", 80m, direction: FinancialDirection.Payable, date: new DateTime(2026, 9, 2));
        var late = Doc("F1", 100m, direction: FinancialDirection.Payable, date: new DateTime(2026, 10, 3));
        var ret = Doc("DEV", 30m, nature: FinancialDocumentNature.PurchaseReturn, direction: FinancialDirection.Payable,
            date: new DateTime(2026, 10, 4));
        var own = Doc("F2", 60m, direction: FinancialDirection.Payable, date: new DateTime(2026, 9, 3));
        var db = await SeedAsync([provisional, early, late, ret, own],
            Offset(early, provisional, 80m, new DateTime(2026, 9, 2)),
            Settle(provisional, 120m, new DateTime(2026, 9, 20)),
            Offset(late, provisional, 100m, new DateTime(2026, 10, 3)),
            Offset(ret, provisional, 30m, new DateTime(2026, 10, 4)),
            Settle(provisional, 30m, new DateTime(2026, 10, 8)),
            Settle(provisional, -10m, new DateTime(2026, 10, 9), FinancialSettlementOrigin.Reversal),
            Settle(own, 25m, new DateTime(2026, 10, 20)));

        var opening = await PositionAsync(db, From.AddDays(-1), FinancialPositionMode.Firm, FinancialDirection.Payable);
        var closing = await PositionAsync(db, To, FinancialPositionMode.Firm, FinancialDirection.Payable);
        var (rows, _) = await RunAsync(db, Request(FinancialDirection.Payable));

        AssertInvariant(rows, opening, closing);
        Assert.Contains(rows, r => r.History == "Abatimento de devolução DEV");
        Assert.Contains(rows, r => r.History == "Abatimento de devolução F1");
        // O dinheiro antigo do provisório entra no F1 no dia em que ele foi composto.
        Assert.Contains(rows, r => r.History == "Baixa F1" && r.Date == "03/10/2026" && r.Debit == 40m);
    }

    [Fact]
    public async Task Provisional_mode_issue_is_each_composition_with_its_date()
    {
        var provisional = Doc("PROV", 500m, nature: FinancialDocumentNature.Provisional, direction: FinancialDirection.Payable,
            date: new DateTime(2026, 9, 1));
        var f0 = Doc("F0", 80m, direction: FinancialDirection.Payable, date: new DateTime(2026, 9, 2), origin: FinancialDocumentOrigin.PurchaseInvoice);
        var f1 = Doc("F1", 100m, direction: FinancialDirection.Payable, date: new DateTime(2026, 10, 3), origin: FinancialDocumentOrigin.PurchaseInvoice);
        var f2 = Doc("F2", 40m, direction: FinancialDirection.Payable, date: new DateTime(2026, 10, 6), origin: FinancialDocumentOrigin.PurchaseInvoice);
        var db = await SeedAsync([provisional, f0, f1, f2],
            Offset(f0, provisional, 80m, new DateTime(2026, 9, 2)),
            Settle(provisional, 50m, new DateTime(2026, 9, 20)),
            Offset(f1, provisional, 100m, new DateTime(2026, 10, 3)),
            Offset(f2, provisional, 40m, new DateTime(2026, 10, 6)),
            Offset(f2, provisional, -40m, new DateTime(2026, 10, 7), FinancialSettlementOrigin.Reversal),
            Settle(provisional, 20m, new DateTime(2026, 10, 9)));

        var opening = await PositionAsync(db, From.AddDays(-1), FinancialPositionMode.Provisional, FinancialDirection.Payable);
        var closing = await PositionAsync(db, To, FinancialPositionMode.Provisional, FinancialDirection.Payable);
        var (rows, _) = await RunAsync(db, Request(FinancialDirection.Payable, FinancialPositionMode.Provisional));

        Assert.Equal(-30m, opening);
        AssertInvariant(rows, opening, closing);
        Assert.Equal(new[] { ("03/10/2026", "Emissão PROV", 100m), ("06/10/2026", "Emissão PROV", 40m), ("07/10/2026", "Emissão PROV", -40m) },
            rows.Where(r => r.History.StartsWith("Emissão")).Select(r => (r.Date, r.History, r.Credit)).ToArray());
    }

    [Fact]
    public async Task Issue_and_settlement_rows_carry_the_invoice_of_their_title()
    {
        var doc = Doc("NF", 100m, origin: FinancialDocumentOrigin.SalesInvoice);
        var invoice = new SalesInvoice { Key = Guid.NewGuid(), CardCode = "C1", TaxDocumentNumber = "293", TaxDocumentSeries = "3" };
        doc.SalesInvoiceKey = invoice.Key;
        var manual = Doc("MAN", 50m);
        var db = await SeedAsync([doc, manual], Settle(doc, 10m, new DateTime(2026, 10, 15)));
        db.Context.SalesInvoices.Add(invoice);
        await db.Context.SaveChangesAsync();

        var (rows, _) = await RunAsync(db);

        Assert.Equal(new[] { ("Emissão NF", "293", "3"), ("Baixa NF", "293", "3") },
            rows.Where(r => r.Document == "NF").Select(r => (r.History, r.NfeNumber, r.NfeSeries)).ToArray());
        var man = rows.Single(r => r.Document == "MAN");
        Assert.Equal(("", ""), (man.NfeNumber, man.NfeSeries));
    }

    [Fact]
    public async Task Generates_pdf()
    {
        var doc = Doc("D1", 300m);
        var invoice = new SalesInvoice { Key = Guid.NewGuid(), CardCode = "C1", TaxDocumentNumber = "000123456", TaxDocumentSeries = "001" };
        doc.SalesInvoiceKey = invoice.Key;
        var db = await SeedAsync([doc], Settle(doc, 10m, new DateTime(2026, 10, 15)));
        db.Context.SalesInvoices.Add(invoice);
        await db.Context.SaveChangesAsync();

        var pdf = await new PartnerStatementReportService(db, FastReport()).ExecuteAsync(Request());

        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(pdf, 0, 4));
        // Amostra só sob demanda (SIAGRO_WRITE_SAMPLE_PDF=1): o teste não deixa lixo no %TEMP%.
        ReportSample.WriteIfRequested("siagro-partner-statement-reports", "PartnerStatement.pdf", pdf);
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
    public async Task Uncomposed_return_own_netting_keeps_invariant_and_closes_at_zero()
    {
        var ret = Doc("DEV", 30m, nature: FinancialDocumentNature.SalesReturn, date: new DateTime(2026, 9, 5));
        var part = Doc("DEV2", 50m, nature: FinancialDocumentNature.SalesReturn, date: new DateTime(2026, 10, 2));
        var db = await SeedAsync([ret, part], Settle(ret, 30m, new DateTime(2026, 10, 15), FinancialSettlementOrigin.Netting),
            Settle(part, 20m, new DateTime(2026, 10, 16), FinancialSettlementOrigin.Netting));

        var opening = await PositionAsync(db, From.AddDays(-1), FinancialPositionMode.Firm, FinancialDirection.Receivable);
        var closing = await PositionAsync(db, To, FinancialPositionMode.Firm, FinancialDirection.Receivable);
        var (rows, _) = await RunAsync(db);

        Assert.Equal((-30m, -30m), (opening, closing));
        AssertInvariant(rows, opening, closing);
        Assert.Equal(new[] { ("Emissão DEV2", 0m, 50m), ("Encontro de contas DEV", 30m, 0m), ("Encontro de contas DEV2", 20m, 0m) },
            rows.Select(r => (r.History, r.Debit, r.Credit)).ToArray());
    }
}
