using Microsoft.Extensions.Configuration;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Services;

namespace SiagroB1.Application.Tests.Reports;

public class FinancialAccountStatementReportServiceTests : IDisposable
{
    private readonly string _contentRoot;

    public FinancialAccountStatementReportServiceTests()
    {
        global::FastReport.Utils.RegisteredObjects.AddConnection(typeof(global::FastReport.Data.MsSqlDataConnection));
        global::FastReport.Utils.Config.WebMode = true;

        _contentRoot = Path.Combine(Path.GetTempPath(), "siagro-account-statement-pdf", Guid.NewGuid().ToString("N"));
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

    private static async Task<IUnitOfWork> SeedAsync()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.FinancialAccounts.Add(new FinancialAccount
        {
            Code = "BCO1", Name = "Banco do Brasil", Type = FinancialAccountType.Bank, Currency = CurrencyType.Brl,
            BankCode = "001", BankName = "BANCO DO BRASIL", BankBranch = "1234-5", BankAccountNumber = "98765-0"
        });
        db.Context.FinancialAccountEntries.AddRange(
            new FinancialAccountEntry { FinancialAccountCode = "BCO1", EntryDate = new DateTime(2026, 9, 1), Amount = 1000m, Description = "Saldo inicial" },
            new FinancialAccountEntry { FinancialAccountCode = "BCO1", EntryDate = new DateTime(2026, 10, 3), Amount = -50m, Description = "Tarifa", DocumentReference = "DOC1", ReconciliationKey = Guid.NewGuid() },
            new FinancialAccountEntry { FinancialAccountCode = "BCO1", EntryDate = new DateTime(2026, 10, 10), Amount = 200m, Description = "Depósito" });
        await db.Context.SaveChangesAsync();
        return db;
    }

    private static FinancialAccountStatementRequest Request(bool onlyReconciled = false) => new()
    {
        AccountCode = "BCO1", DateFrom = new DateTime(2026, 10, 1), DateTo = new DateTime(2026, 10, 31), OnlyReconciled = onlyReconciled
    };

    [Fact]
    public async Task Statement_rows_carry_running_balance()
    {
        var db = await SeedAsync();
        var recorder = new RecordingFastReportService();

        await new FinancialAccountStatementReportService(db, recorder).ExecuteAsync(Request());

        var rows = Assert.IsAssignableFrom<ICollection<FinancialAccountStatementRowDto>>(recorder.LastData).ToList();
        Assert.Equal("FinancialAccountStatement.frx", recorder.LastReportName);
        Assert.Equal(2, rows.Count);
        Assert.Equal(("03/10/2026", 0m, 50m, 950m, "Sim"), (rows[0].Date, rows[0].In, rows[0].Out, rows[0].Balance, rows[0].Reconciled));
        Assert.Equal(("10/10/2026", 200m, 0m, 1150m, "Não"), (rows[1].Date, rows[1].In, rows[1].Out, rows[1].Balance, rows[1].Reconciled));
        Assert.Equal(1000m, recorder.LastParameters!["pOpeningBalance"]);
        Assert.Equal(1150m, recorder.LastParameters["pClosingBalance"]);
        Assert.Equal("BCO1 - Banco do Brasil", recorder.LastParameters["pAccount"]);
        Assert.Equal("Banco 001 BANCO DO BRASIL · Agência 1234-5 · Conta 98765-0", recorder.LastParameters["pBankInfo"]);
        Assert.Equal("Período: 01/10/2026 a 31/10/2026", recorder.LastParameters["pPeriod"]);
    }

    [Fact]
    public async Task OnlyReconciled_filters_rows()
    {
        var db = await SeedAsync();
        var recorder = new RecordingFastReportService();

        await new FinancialAccountStatementReportService(db, recorder).ExecuteAsync(Request(onlyReconciled: true));

        var rows = Assert.IsAssignableFrom<ICollection<FinancialAccountStatementRowDto>>(recorder.LastData);
        Assert.Equal("Tarifa", Assert.Single(rows).Description);
        Assert.Equal("Período: 01/10/2026 a 31/10/2026 · somente conciliados", recorder.LastParameters!["pPeriod"]);
    }

    [Fact]
    public async Task Unknown_account_is_refused()
    {
        var db = TestDb.CreateUnitOfWork();
        var e = await Assert.ThrowsAsync<ApplicationException>(
            () => new FinancialAccountStatementReportService(db, new RecordingFastReportService()).ExecuteAsync(Request()));
        Assert.Equal("Conta financeira BCO1 não encontrada.", e.Message);
    }

    [Fact]
    public void Validate_requires_account_and_ordered_period()
    {
        Assert.Equal("Informe a conta financeira.",
            FinancialAccountStatementReportService.Validate(new FinancialAccountStatementRequest { DateFrom = DateTime.Today, DateTo = DateTime.Today }));
        Assert.Equal("A data inicial deve ser menor ou igual à final.",
            FinancialAccountStatementReportService.Validate(new FinancialAccountStatementRequest { AccountCode = "X", DateFrom = DateTime.Today, DateTo = DateTime.Today.AddDays(-1) }));
        Assert.Null(FinancialAccountStatementReportService.Validate(Request()));
    }

    [Fact]
    public async Task Statement_produces_a_pdf()
    {
        var db = await SeedAsync();

        var pdf = await new FinancialAccountStatementReportService(db, FastReport()).ExecuteAsync(Request());

        Keep("with-rows", pdf);
    }

    [Fact]
    public async Task Empty_period_renders_with_opening_equal_closing()
    {
        var db = await SeedAsync();
        var request = Request();
        request.DateFrom = new DateTime(2026, 12, 1);
        request.DateTo = new DateTime(2026, 12, 31);

        var pdf = await new FinancialAccountStatementReportService(db, FastReport()).ExecuteAsync(request);

        Keep("empty", pdf);
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

    private static void Keep(string name, byte[] pdf)
    {
        Assert.NotEmpty(pdf);
        var folder = Path.Combine(Path.GetTempPath(), "siagro-account-statement-reports");
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, $"FinancialAccountStatement-{name}.pdf"), pdf);
    }
}
