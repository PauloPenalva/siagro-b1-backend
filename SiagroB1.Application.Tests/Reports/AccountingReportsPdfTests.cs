using Microsoft.Extensions.Configuration;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Services;

namespace SiagroB1.Application.Tests.Reports;

/// <summary>Renderiza Razão e Balancete com o FastReport de verdade (template válido, bandas e parâmetros).</summary>
public class AccountingReportsPdfTests : IDisposable
{
    private readonly string _contentRoot;

    public AccountingReportsPdfTests()
    {
        global::FastReport.Utils.RegisteredObjects.AddConnection(typeof(global::FastReport.Data.MsSqlDataConnection));
        global::FastReport.Utils.Config.WebMode = true;

        _contentRoot = Path.Combine(Path.GetTempPath(), "siagro-accounting-pdf", Guid.NewGuid().ToString("N"));
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

    private static async Task<SiagroB1.Infra.IUnitOfWork> SeedAsync()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.LedgerAccounts.AddRange(
            new LedgerAccount { Code = "1", Name = "Ativo", Type = LedgerAccountType.Asset, AllowsPosting = false },
            new LedgerAccount { Code = "1.1.01", Name = "Caixa", Type = LedgerAccountType.Asset, ParentCode = "1" },
            new LedgerAccount { Code = "3.1.01", Name = "Vendas", Type = LedgerAccountType.Revenue });
        db.Context.JournalEntries.Add(new JournalEntry
        {
            PostingDate = new DateTime(2026, 10, 5), BranchCode = "01", Batch = "008890", DocumentNumber = "000001",
            Origin = JournalEntryOrigin.Manual, CreatedBy = "t",
            Lines =
            [
                new JournalEntryLine { LineNumber = 1, EntryType = JournalEntryLineType.Double, DebitAccountCode = "1.1.01", CreditAccountCode = "3.1.01", Amount = 1234.56m, History = "Venda à vista" },
            ],
        });
        await db.SaveChangesAsync();
        return db;
    }

    [Fact]
    public async Task Ledger_renders_pdf()
    {
        var pdf = await new LedgerReportService(await SeedAsync(), FastReport()).ExecuteAsync(new LedgerReportRequest
        {
            DateFrom = new DateTime(2026, 10, 1), DateTo = new DateTime(2026, 10, 31),
        });
        Keep("ledger", pdf);
    }

    [Fact]
    public async Task TrialBalance_renders_pdf()
    {
        var pdf = await new TrialBalanceReportService(await SeedAsync(), FastReport()).ExecuteAsync(new TrialBalanceReportRequest
        {
            DateFrom = new DateTime(2026, 10, 1), DateTo = new DateTime(2026, 10, 31),
        });
        Keep("trial-balance", pdf);
    }

    [Fact]
    public async Task IncomeStatement_renders_pdf()
    {
        var pdf = await new IncomeStatementReportService(await AccountingStatementsTests.SeedAsync(), FastReport())
            .ExecuteAsync(new IncomeStatementReportRequest
            {
                DateFrom = new DateTime(2026, 10, 1), DateTo = new DateTime(2026, 10, 31), ViewCode = "DRE",
            });
        Keep("income-statement", pdf);
    }

    [Fact]
    public async Task BalanceSheet_renders_pdf()
    {
        var pdf = await new BalanceSheetReportService(await AccountingStatementsTests.SeedAsync(), FastReport())
            .ExecuteAsync(new BalanceSheetReportRequest
            {
                Date = new DateTime(2026, 10, 31), CompareDate = new DateTime(2026, 9, 30), ViewCode = "BP",
            });
        Keep("balance-sheet", pdf);
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
        var folder = Path.Combine(Path.GetTempPath(), "siagro-accounting-reports");
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, $"Accounting-{name}.pdf"), pdf);
    }
}
