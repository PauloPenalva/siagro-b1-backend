using Microsoft.Extensions.Configuration;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Services;
using static SiagroB1.Application.Tests.Support.ContractPositionSeed;

namespace SiagroB1.Application.Tests.Reports;

/// <summary>
/// Gera o PDF de ponta a ponta com o template real, nos dois modos (sem coluna fiscal: os dois
/// modos provam que nada depende de tabela do SAP). Pega fonte de dados com nome divergente, coluna
/// que o FastReport não converte e grupo aninhado quebrado. Grava em %TEMP%/siagro-contract-position-reports.
/// </summary>
public class ContractPositionReportsPdfTests : IDisposable
{
    private readonly string _contentRoot;

    public ContractPositionReportsPdfTests()
    {
        global::FastReport.Utils.RegisteredObjects.AddConnection(typeof(global::FastReport.Data.MsSqlDataConnection));
        global::FastReport.Utils.Config.WebMode = true;

        _contentRoot = Path.Combine(Path.GetTempPath(), "siagro-contract-position-pdf", Guid.NewGuid().ToString("N"));
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

    [Theory]
    [InlineData("STANDALONE")]
    [InlineData("SAPB1")]
    public async Task ContractPosition_ProducesAPdf(string erp)
    {
        var db = await SeedMixedAsync();

        var pdf = await new ContractPositionReportService(db, FastReport(Configuration(erp)))
            .ExecuteAsync(new ContractPositionRequest(), Today);

        Keep("ContractPosition", erp, pdf);
    }

    [Fact]
    public async Task ContractPosition_EmptyResultStillProducesAPdf()
    {
        var pdf = await new ContractPositionReportService(TestDb.CreateUnitOfWork(), FastReport(Configuration("STANDALONE")))
            .ExecuteAsync(new ContractPositionRequest(), Today);

        Assert.NotEmpty(pdf);
    }

    // Valores grandes e negativos, uma UM só (total geral visível): exercitam as larguras.
    [Fact]
    public async Task ContractPosition_LargeValuesProduceAPdf()
    {
        var db = TestDb.CreateUnitOfWork();
        var purchase = Purchase("PC2026000123", total: 99_999_999.999m, price: 9_999_999.99m,
            cardName: "COOPERATIVA AGROINDUSTRIAL DOS PRODUTORES RURAIS DO NORTE DO PARANÁ",
            status: ContractStatus.InApproval, harvest: "2025/2026", cashFlow: new DateTime(2026, 12, 31));
        var sale = Sales("CV2026000123", total: 10_000_000m, price: 9_999_999.99m);
        var item = SalesItem(20_000_000m);
        db.Context.PurchaseContracts.Add(purchase);
        db.Context.SalesContracts.Add(sale);
        db.Context.SalesInvoicesItems.Add(item);
        db.Context.PurchaseContractsAllocations.Add(PurchaseAllocation(purchase, 88_888_888.888m));
        db.Context.PurchaseContractsWashouts.Add(Washout(purchase, 1, 9_999_999.999m));
        db.Context.SalesContractsAllocations.Add(SalesAllocation(sale, item, 20_000_000m)); // saldo −10.000.000
        await Save(db);

        var pdf = await new ContractPositionReportService(db, FastReport(Configuration("STANDALONE")))
            .ExecuteAsync(new ContractPositionRequest { Statuses = [ContractStatus.Approved, ContractStatus.InApproval] }, Today);

        Keep("ContractPosition-large", "STANDALONE", pdf);
    }

    /// <summary>Soja KG (compra com washout e venda com quebra), milho TN, um contrato sem prazo.</summary>
    private static async Task<IUnitOfWork> SeedMixedAsync()
    {
        var db = TestDb.CreateUnitOfWork();
        var purchase = Purchase("PC000001", total: 100_000m, cashFlow: new DateTime(2026, 11, 10),
            deliveryEnd: new DateTime(2026, 11, 30));
        var paf = Purchase("PC000002", total: 50_000m, type: ContractType.ToBeDetermined, deliveryEnd: new DateTime(2026, 9, 30));
        var sale = Sales("CV000001", total: 80_000m, deliveryEnd: new DateTime(2026, 12, 31));
        var noDeadline = Sales("CV000002", total: 10_000m, deliveryEnd: DateTime.MinValue, cardName: "AGRO NORTE");
        var corn = Purchase("PC000003", total: 300m, itemCode: "20001", itemName: "MILHO", uom: "TN");
        var item = SalesItem(30_000m, closed: true, delivered: 29_500m, loss: 100m);
        db.Context.PurchaseContracts.AddRange(purchase, paf, corn);
        db.Context.SalesContracts.AddRange(sale, noDeadline);
        db.Context.SalesInvoicesItems.Add(item);
        db.Context.PurchaseContractsAllocations.AddRange(
            PurchaseAllocation(purchase, 40_000m), PurchaseAllocation(purchase, -2_000m));
        db.Context.PurchaseContractsWashouts.Add(Washout(paf, 1, 0m, 5_000m));
        db.Context.SalesContractsAllocations.Add(SalesAllocation(sale, item, 30_000m));
        await Save(db);
        return db;
    }

    private FastReportService FastReport(IConfiguration configuration)
    {
        var env = new TestWebHostEnvironment(_contentRoot);
        return new FastReportService(env, configuration,
            new ReportHeaderService(env, configuration, new TestLogger<ReportHeaderService>()));
    }

    private static IConfiguration Configuration(string erp) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Erp"] = erp,
                ["CompanyName"] = "ACME AGRO LTDA",
                ["CompanyLogoPath"] = "wwwroot/images/logo.png",
            })
            .Build();

    private static async Task Save(IUnitOfWork db)
    {
        await db.Context.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();
    }

    private static void Keep(string report, string erp, byte[] pdf)
    {
        Assert.NotEmpty(pdf);
        var folder = Path.Combine(Path.GetTempPath(), "siagro-contract-position-reports");
        Directory.CreateDirectory(folder);
        File.WriteAllBytes(Path.Combine(folder, $"{report}-{erp}.pdf"), pdf);
    }
}
