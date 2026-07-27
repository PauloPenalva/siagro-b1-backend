using Microsoft.Extensions.Configuration;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Services;

namespace SiagroB1.Application.Tests.Reports;

/// <summary>
/// Gera o PDF de venda de ponta a ponta com o template real. Pega erros que só
/// aparecem na junção serviço + .frx, como fonte de dados com nome divergente
/// (GetDataSource devolvendo null) ou coluna que o FastReport não converte.
/// </summary>
public class SalesContractsByItemPdfTests : IDisposable
{
    private readonly string _contentRoot;

    public SalesContractsByItemPdfTests()
    {
        FastReport.Utils.RegisteredObjects.AddConnection(typeof(FastReport.Data.MsSqlDataConnection));
        FastReport.Utils.Config.WebMode = true;

        // FastReportService procura o template em <ContentRoot>/Reports/Templates.
        _contentRoot = Path.Combine(Path.GetTempPath(), "siagro-scbi-pdf", Guid.NewGuid().ToString("N"));
        var templates = Path.Combine(_contentRoot, "Reports", "Templates");
        Directory.CreateDirectory(templates);
        File.Copy(
            Path.Combine(AppContext.BaseDirectory, "ReportTemplates", "SalesContractsByItem.frx"),
            Path.Combine(templates, "SalesContractsByItem.frx"));

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

    [Fact]
    public async Task ExecuteAsync_ProducesANonEmptyPdf()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.Branchs.Add(new Branch { Code = "01", BranchName = "MATRIZ LTDA", ShortName = "MATRIZ" });
        db.Context.LogisticRegions.Add(new LogisticRegion { Code = "RL01", Name = "PORTO DE PARANAGUA" });
        db.Context.SalesContracts.Add(new SalesContract
        {
            Key = Guid.NewGuid(),
            Code = "CV-1",
            CreationDate = new DateTime(2026, 7, 15),
            Status = ContractStatus.Approved,
            MarketType = MarketType.External,
            ItemCode = "10001",
            ItemName = "SOJA EM GRÃOS",
            HarvestSeasonCode = "2026",
            BranchCode = "01",
            LogisticRegionCode = "RL01",
            CardCode = "C001",
            CardName = "COOPERATIVA CENTRAL",
            AgentName = "Carlos Dias",
            UnitOfMeasureCode = "TN",
            TotalVolume = 1500m,
            Price = 128.5m,
            FreightTerms = FreightTerms.Cif,
            DeliveryStartDate = new DateTime(2026, 8, 1),
            DeliveryEndDate = new DateTime(2026, 8, 31),
        });
        await db.Context.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["CompanyName"] = "ACME AGRO LTDA",
                ["CompanyLogoPath"] = "wwwroot/images/logo.png"
            })
            .Build();

        var env = new TestWebHostEnvironment(_contentRoot);
        var fastReport = new FastReportService(
            env,
            configuration,
            new ReportHeaderService(env, configuration, new TestLogger<ReportHeaderService>()));

        var pdf = await new SalesContractsByItemReportService(db, fastReport)
            .ExecuteAsync(new SalesContractsByItemRequest
            {
                FromDate = new DateTime(2026, 7, 1),
                ToDate = new DateTime(2026, 7, 31),
            });

        Assert.NotEmpty(pdf);
    }
}
