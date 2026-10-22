using FastReport;
using Microsoft.Extensions.Configuration;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;
using SiagroB1.Infra.FiscalStock;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Services;
using static SiagroB1.Application.Tests.Support.InvoiceReportSeed;

namespace SiagroB1.Application.Tests.Reports;

/// <summary>Livro Registro Kardex — modelo 3 (base MATR470): fichas produto × mês, termos e folhas numeradas.</summary>
public class FiscalKardexBookTests : IDisposable
{
    private static readonly DateTime Sep30 = new(2026, 9, 30);
    private readonly string _contentRoot;

    public FiscalKardexBookTests()
    {
        global::FastReport.Utils.Config.WebMode = true;
        _contentRoot = Path.Combine(Path.GetTempPath(), "siagro-kardex-book", Guid.NewGuid().ToString("N"));
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

    [Fact]
    public async Task Rows_OneSheetPerMonthWithOpeningLineAndTotals()
    {
        var db = TestDb.CreateUnitOfWork();
        await Seed(db);

        var rows = await Service(db).BookRowsAsync("01", null, new DateTime(2026, 10, 1), new DateTime(2026, 11, 30));

        var october = rows.Where(r => r.SheetKey == "2026-10|10001").ToList();
        Assert.Equal(3, october.Count);
        Assert.True(october[0].IsOpening);
        Assert.Equal("SALDO INICIAL:", october[0].LedgerAccount);
        Assert.Equal("10,000", october[0].BalanceText);
        Assert.Equal("10/2026", october[0].Period);
        Assert.Equal("12019000", october[0].Ncm);

        var entry = october[1];
        Assert.Equal(("NFE", "1", "000123", "05/10/2026", "05"), (entry.Species, entry.Series, entry.Number, entry.DateText, entry.Day));
        Assert.Equal(("1130100002", "1101", "E", "1"), (entry.LedgerAccount, entry.Cfop, entry.Direction, entry.Code));
        Assert.Equal(("10,000", "1.200,00", "0,00", "20,000"), (entry.QuantityText, entry.ValueText, entry.IpiText, entry.BalanceText));

        var sale = october[2];
        Assert.Equal(("", "5101", "S", "1"), (sale.LedgerAccount, sale.Cfop, sale.Direction, sale.Code));
        Assert.Equal(("15,000", "1.600,00", "5,000"), (sale.QuantityText, sale.ValueText, sale.BalanceText));

        Assert.All(october, r => Assert.Equal(("10,000", "1.200,00", "15,000", "1.600,00", "5,000"),
            (r.TotalInQuantityText, r.TotalInValueText, r.TotalOutQuantityText, r.TotalOutValueText, r.ClosingBalanceText)));

        var november = rows.Where(r => r.SheetKey == "2026-11|10001").ToList();
        Assert.Equal(2, november.Count);
        Assert.Equal("5,000", november[0].BalanceText);
        var adjustment = november[1];
        Assert.Equal(("", "", "AJ-1", "S", "3"), (adjustment.Species, adjustment.Series, adjustment.Number, adjustment.Direction, adjustment.Code));
        Assert.Equal("", adjustment.LedgerAccount);
        Assert.Equal("Ajuste manual: quebra técnica", adjustment.Notes);
        Assert.Equal("3,000", adjustment.BalanceText);
    }

    [Fact]
    public async Task Rows_ItemFilterAndPeriodCut()
    {
        var db = TestDb.CreateUnitOfWork();
        await Seed(db);

        var rows = await Service(db).BookRowsAsync("01", "10001", new DateTime(2026, 10, 6), new DateTime(2026, 10, 31));

        // só a venda dentro do período; o saldo inicial do recorte já inclui a entrada do dia 5
        Assert.Equal(2, rows.Count);
        Assert.Equal("20,000", rows[0].BalanceText);
        Assert.Equal("S", rows[1].Direction);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    public async Task Book_RefusesInvalidNumbers(int book, int sheet)
    {
        var db = TestDb.CreateUnitOfWork();
        await Seed(db);

        var ex = await Assert.ThrowsAsync<BusinessException>(() => Service(db).BookAsync(Request(book, sheet)));
        Assert.Equal("Informe o número do livro e a folha inicial.", ex.Message);
    }

    [Fact]
    public async Task Book_RefusesEmptyPeriod()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.Municipalities.Add(new Municipality { Code = "5107602", Name = "Rondonópolis", StateAbbreviation = "MT" });
        db.Context.Branchs.Add(FiscalInventoryBookTests.FullBranch());
        await Save(db);

        var ex = await Assert.ThrowsAsync<BusinessException>(() => Service(db).BookAsync(Request()));
        Assert.Equal("Não há movimento de estoque no período.", ex.Message);
    }

    [Fact]
    public async Task Book_RendersPdf()
    {
        var db = TestDb.CreateUnitOfWork();
        await Seed(db);

        var pdf = await Service(db).BookAsync(Request());

        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(pdf, 0, 4));
    }

    [Fact]
    public async Task Book_TermsSheetsAndOneSheetPerProductMonth()
    {
        var db = TestDb.CreateUnitOfWork();
        await Seed(db);

        using var report = await Service(db).PrepareBookAsync(Request(book: 2, sheet: 41));
        var count = report.PreparedPages.Count;
        Assert.Equal(4, count); // abertura + outubro + novembro + encerramento

        for (var i = 0; i < count; i++)
        {
            var sheets = report.PreparedPages.GetPage(i).AllObjects.OfType<TextObject>()
                .Where(t => t.Name.StartsWith("txtSheet", StringComparison.Ordinal)).Select(t => t.Text).ToList();
            Assert.Equal([$"Folha nº {41 + i}"], sheets);
        }

        Assert.Contains("Contém este livro 4 folhas numeradas tipograficamente, do nº 41 ao nº 44, e servirá para o lançamento do Registro de Controle da Produção e do Estoque do período de 01/10/2026 a 30/11/2026 de ACME AGRO LTDA",
            Text(report, 0, "txtOpeningTerm"));
        Assert.Contains("e serviu para o lançamento", Text(report, 3, "txtClosingTerm"));

        Assert.Equal("MÊS OU PERÍODO/ANO: 10/2026", Text(report, 1, "txtPeriod"));
        Assert.Equal("MÊS OU PERÍODO/ANO: 11/2026", Text(report, 2, "txtPeriod"));
        Assert.Equal(["", "000123", "V1"], Texts(report, 1, "txtNumber"));
        Assert.Equal("10,000", Text(report, 1, "txtTotalInQuantity"));
        Assert.Equal("1.600,00", Text(report, 1, "txtTotalOutValue"));
        Assert.Equal("5,000", Text(report, 1, "txtTotalStockBalance"));
        Assert.Equal("3,000", Text(report, 2, "txtTotalStockBalance"));
    }

    // ---- apoio ----

    private static List<string> Texts(Report report, int page, string name) =>
        report.PreparedPages.GetPage(page).AllObjects.OfType<TextObject>()
            .Where(t => t.Name == name).Select(t => t.Text).ToList();

    private static string Text(Report report, int page, string name)
    {
        var texts = Texts(report, page, name);
        Assert.True(texts.Count == 1, $"página {page}: {texts.Count} objetos '{name}'");
        return texts[0];
    }

    private static FiscalKardexBookRequest Request(int book = 1, int sheet = 1) => new()
    {
        BranchCode = "01", FromDate = new DateTime(2026, 10, 1), ToDate = new DateTime(2026, 11, 30),
        BookNumber = book, FirstSheet = sheet,
    };

    /// <summary>Saldo 10 em 30/09; outubro: compra 10 (nota 000123/1) e venda 15; novembro: ajuste de saída 2.</summary>
    private static async Task Seed(IUnitOfWork db)
    {
        db.Context.Municipalities.Add(new Municipality { Code = "5107602", Name = "Rondonópolis", StateAbbreviation = "MT" });
        db.Context.Branchs.Add(FiscalInventoryBookTests.FullBranch());
        db.Context.Items.Add(new Item { ItemCode = "10001", ItemName = "SOJA EM GRÃOS", Ncm = "12019000", LedgerAccountCode = "1130100002" });
        db.Context.FiscalInventoryOpeningBalances.Add(new FiscalInventoryOpeningBalance
        {
            Key = Guid.NewGuid(), BranchCode = "01", ItemCode = "10001", ItemName = "SOJA EM GRÃOS",
            BalanceDate = Sep30, Quantity = 10m, TotalValue = 1000m, UnitOfMeasureCode = "TN",
        });

        var buyItem = PurchaseItem(quantity: 10m, unitPrice: 120m);
        buyItem.Cfop = "1101";
        buyItem.MovesFiscalInventory = true;
        var buy = Purchase("C1", new DateTime(2026, 10, 5), branchCode: "01", items: [buyItem]);
        buy.TaxDocumentNumber = "000123";
        buy.TaxDocumentSeries = "1";
        db.Context.PurchaseInvoices.Add(buy);

        var saleItem = SaleItem(quantity: 15m, unitPrice: 200m);
        saleItem.Cfop = "5101";
        saleItem.MovesFiscalInventory = true;
        var sale = Sale("V1", new DateTime(2026, 10, 10), items: saleItem);
        sale.NfeStatus = NfeStatus.Authorized;
        db.Context.SalesInvoices.Add(sale);

        db.Context.FiscalStockAdjustments.Add(new FiscalStockAdjustment
        {
            BranchCode = "01", ItemCode = "10001", ItemName = "SOJA EM GRÃOS", UnitOfMeasureCode = "TN",
            AdjustmentDate = new DateTime(2026, 11, 3), Direction = FiscalStockAdjustmentDirection.Out,
            Quantity = 2m, TotalValue = 200m, Reason = "quebra técnica", DocNumber = "AJ-1",
        });
        await Save(db);
    }

    private static async Task Save(IUnitOfWork db)
    {
        await db.Context.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();
    }

    private FiscalKardexBookService Service(IUnitOfWork db)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Erp"] = "STANDALONE", ["CompanyName"] = "ACME" })
            .Build();
        var env = new TestWebHostEnvironment(_contentRoot);
        var fastReport = new FastReportService(env, configuration,
            new ReportHeaderService(env, configuration, new TestLogger<ReportHeaderService>(), FakeCompanyContext.Unresolved()),
            FakeCompanyContext.Unresolved());
        return new FiscalKardexBookService(db, new FiscalKardexService(new FiscalStockLedgerLoader(db)), fastReport);
    }
}
