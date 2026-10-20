using System.Globalization;
using FastReport;
using Microsoft.Extensions.Configuration;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Services;

namespace SiagroB1.Application.Tests.Reports;

/// <summary>
/// Livro de Registro de Inventário (modelo 7). A numeração de folhas e o "a transportar"/"transporte"
/// são conferidos nas páginas preparadas do FastReport (o PDF simples é imagem, sem texto).
/// </summary>
public class FiscalInventoryBookTests : IDisposable
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");
    private static readonly DateTime Sep30 = new(2026, 9, 30);
    private static readonly DateTime Dec31 = new(2026, 12, 31);
    private readonly string _contentRoot;

    public FiscalInventoryBookTests()
    {
        global::FastReport.Utils.Config.WebMode = true;
        _contentRoot = Path.Combine(Path.GetTempPath(), "siagro-inventory-book", Guid.NewGuid().ToString("N"));
        var templates = Path.Combine(_contentRoot, "Reports", "Templates");
        Directory.CreateDirectory(templates);
        foreach (var file in Directory.GetFiles(Path.Combine(AppContext.BaseDirectory, "ReportTemplates"), "*.frx"))
            File.Copy(file, Path.Combine(templates, Path.GetFileName(file)));
    }

    public void Dispose()
    {
        if (Directory.Exists(_contentRoot))
            Directory.Delete(_contentRoot, recursive: true);
        GC.SuppressFinalize(this);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    public async Task Book_RefusesInvalidNumbers(int book, int sheet)
    {
        var db = TestDb.CreateUnitOfWork();
        await Seed(db, items: 1);

        var ex = await Assert.ThrowsAsync<BusinessException>(() =>
            Service(db).BookAsync(Request(book, sheet)));
        Assert.Equal("Informe o número do livro e a folha inicial.", ex.Message);
    }

    [Fact]
    public async Task Book_RefusesWithoutBranchData()
    {
        var db = TestDb.CreateUnitOfWork();
        await Seed(db, items: 1, branch: new Branch { Code = "01", BranchName = "MATRIZ", TaxId = "12345678000195" });

        var ex = await Assert.ThrowsAsync<BusinessException>(() => Service(db).BookAsync(Request()));
        Assert.Equal("Complete o cadastro da filial antes de imprimir: Razão social, Inscrição Estadual.", ex.Message);
    }

    [Fact]
    public async Task Book_RefusesEmptyInventory()
    {
        var db = TestDb.CreateUnitOfWork();
        await Seed(db, items: 0);

        var ex = await Assert.ThrowsAsync<BusinessException>(() => Service(db).BookAsync(Request()));
        Assert.Equal("Não há saldo a inventariar nesta data.", ex.Message);
    }

    [Fact]
    public async Task Book_RendersPdf()
    {
        var db = TestDb.CreateUnitOfWork();
        await Seed(db, items: 130);

        var pdf = await Service(db).BookAsync(Request(book: 3, sheet: 51));

        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(pdf, 0, 4));
        var folder = Path.Combine(Path.GetTempPath(), "siagro-invoice-reports");
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, "FiscalInventoryBook.pdf"), pdf);
    }

    [Fact]
    public async Task Book_SheetNumbersStartAtFirstSheet()
    {
        var db = TestDb.CreateUnitOfWork();
        await Seed(db, items: 130);

        using var report = await Service(db).PrepareBookAsync(Request(book: 3, sheet: 51));
        var count = report.PreparedPages.Count;
        Assert.True(count >= 5, $"esperava termo + 3 páginas + termo, veio {count}");

        for (var i = 0; i < count; i++)
        {
            // cada página (termos e inventário) tem um único objeto de folha: txtSheet*
            var sheets = report.PreparedPages.GetPage(i).AllObjects.OfType<TextObject>()
                .Where(t => t.Name.StartsWith("txtSheet", StringComparison.Ordinal)).Select(t => t.Text).ToList();
            Assert.Equal([$"Folha nº {51 + i}"], sheets);
        }

        var last = 51 + count - 1;
        Assert.Contains($"Contém este livro {count} folhas numeradas tipograficamente, do nº 51 ao nº {last}, e servirá",
            Text(report, 0, "txtOpeningTerm"));
        Assert.Contains($"Contém este livro {count} folhas numeradas tipograficamente, do nº 51 ao nº {last}, e serviu",
            Text(report, count - 1, "txtClosingTerm"));
        Assert.Contains("ACME AGRO LTDA", Text(report, 0, "txtOpeningTerm"));
        Assert.Contains("Livro nº 3", Text(report, 1, "txtBookNumber"));
        Assert.Contains("Inventário existente em 31/12/2026", Text(report, 1, "txtInventoryDate"));
    }

    [Fact]
    public async Task Book_CarryForwardMatchesNextPage()
    {
        var db = TestDb.CreateUnitOfWork();
        await Seed(db, items: 130);

        using var report = await Service(db).PrepareBookAsync(Request());
        var count = report.PreparedPages.Count;
        var running = 0m;
        // páginas do inventário: 1 .. count-2
        for (var page = 1; page <= count - 2; page++)
        {
            var carryIn = Text(report, page, "txtCarryIn");
            if (page == 1)
                Assert.Equal("", carryIn);
            else
                Assert.Equal(Money(running), carryIn);

            var values = Texts(report, page, "txtValue").Select(t => decimal.Parse(t, PtBr)).ToList();
            Assert.NotEmpty(values);
            running += values.Sum();

            var carryOut = Text(report, page, "txtCarryOut");
            Assert.Equal(page == count - 2 ? "" : Money(running), carryOut);
        }
    }

    [Fact]
    public async Task Book_GrandTotalEqualsSumOfRows()
    {
        var db = TestDb.CreateUnitOfWork();
        await Seed(db, items: 130);

        var data = await Service(db).BuildAsync(new FiscalInventoryRequest { BranchCode = "01", Date = Dec31 });
        using var report = await Service(db).PrepareBookAsync(Request());
        var count = report.PreparedPages.Count;

        var all = Enumerable.Range(1, count - 2).SelectMany(p => Texts(report, p, "txtValue"))
            .Select(t => decimal.Parse(t, PtBr)).ToList();
        Assert.Equal(data.Rows.Count, all.Count);
        Assert.Equal(data.TotalValue, all.Sum());
        Assert.Equal(Money(data.TotalValue), Text(report, count - 2, "txtGrandTotal"));
    }

    [Fact]
    public async Task Book_NcmSubtotalOnLastLineOfGroup()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.Branchs.Add(FullBranch());
        // 2 no NCM A, 1 no NCM B, 1 sem NCM
        Add(db, "I1", "10019900", 10m, 100m);
        Add(db, "I2", "10019900", 5m, 55.5m);
        Add(db, "I3", "12019000", 1m, 7m);
        Add(db, "I4", null, 2m, 3m);
        await Save(db);

        var rows = await Service(db).BookRowsAsync("01", Dec31);

        Assert.Equal(["I1", "I2", "I3", "I4"], rows.Select(r => r.ItemCode));
        Assert.Equal([null, 155.5m, 7m, 3m], rows.Select(r => r.NcmGroupTotal));

        using var report = await Service(db).PrepareBookAsync(Request());
        Assert.Equal(["", "155,50", "7,00", "3,00"], Texts(report, 1, "txtNcmTotal"));
    }

    // ---- apoio ----

    private static string Money(decimal value) => value.ToString("N2", PtBr);

    private static List<string> Texts(Report report, int page, string name) =>
        report.PreparedPages.GetPage(page).AllObjects.OfType<TextObject>()
            .Where(t => t.Name == name).Select(t => t.Text).ToList();

    private static string Text(Report report, int page, string name)
    {
        var texts = Texts(report, page, name);
        Assert.True(texts.Count == 1, $"página {page}: {texts.Count} objetos '{name}'");
        return texts[0];
    }

    private static FiscalInventoryBookRequest Request(int book = 1, int sheet = 1) => new()
    {
        BranchCode = "01", Date = Dec31, BookNumber = book, FirstSheet = sheet,
    };

    internal static Branch FullBranch() => new()
    {
        Code = "01", BranchName = "MATRIZ", LegalName = "ACME AGRO LTDA", TaxId = "12345678000195",
        StateRegistration = "9012345678", Street = "RODOVIA BR 163", StreetNumber = "KM 10", District = "ZONA RURAL",
        ZipCode = "78890000", MunicipalityCode = "5107602",
    };

    private static async Task Seed(IUnitOfWork db, int items, Branch? branch = null)
    {
        db.Context.Municipalities.Add(new Municipality { Code = "5107602", Name = "Rondonópolis", StateAbbreviation = "MT" });
        db.Context.Branchs.Add(branch ?? FullBranch());
        string[] ncms = ["10019900", "10059010", "12019000", "23040010"];
        for (var i = 1; i <= items; i++)
            Add(db, $"P{i:000}", ncms[i * ncms.Length / (items + 1)], i, i * 10.37m + 0.01m);
        await Save(db);
    }

    private static void Add(IUnitOfWork db, string code, string? ncm, decimal qty, decimal value)
    {
        db.Context.Items.Add(new Item { ItemCode = code, ItemName = "PRODUTO " + code, Ncm = ncm });
        db.Context.FiscalInventoryOpeningBalances.Add(new FiscalInventoryOpeningBalance
        {
            Key = Guid.NewGuid(), BranchCode = "01", ItemCode = code, ItemName = "PRODUTO " + code,
            BalanceDate = Sep30, Quantity = qty, TotalValue = value, UnitOfMeasureCode = "TN",
        });
    }

    private static async Task Save(IUnitOfWork db)
    {
        await db.Context.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();
    }

    private FiscalInventoryService Service(IUnitOfWork db)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Erp"] = "STANDALONE", ["CompanyName"] = "ACME" })
            .Build();
        var env = new TestWebHostEnvironment(_contentRoot);
        var fastReport = new FastReportService(env, configuration,
            new ReportHeaderService(env, configuration, new TestLogger<ReportHeaderService>(), FakeCompanyContext.Unresolved()),
            FakeCompanyContext.Unresolved());
        return new FiscalInventoryService(db, new FiscalKardexService(db), fastReport);
    }
}
