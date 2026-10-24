using SiagroB1.Application.Services.DocNumbers;
using SiagroB1.Application.Services.FreightOrders;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.FreightOrders;

public class FreightOrdersSaveServiceTests
{
    private static FreightOrderSaveModel Model(Action<FreightOrderSaveModel>? tweak = null)
    {
        var m = new FreightOrderSaveModel
        {
            BranchCode = "01", TakerCardCode = "C1", StartMunicipalityCode = "3550308", StartState = "SP",
            EndMunicipalityCode = "4106902", EndState = "PR", ItemCode = "SOJA", ItemName = "SOJA",
            ContractedWeightKg = 50_000m, UnitPrice = 100m, ValidFrom = new DateTime(2026, 1, 1),
        };
        tweak?.Invoke(m);
        return m;
    }

    private static (UnitOfWork Db, FreightOrdersSaveService Svc) Arrange(string erp = "STANDALONE")
    {
        var db = TestDb.CreateUnitOfWork();
        var svc = new FreightOrdersSaveService(db, new FakeDocNumberSequenceService(),
            new FakeBusinessPartnerService(new Dictionary<string, string> { ["C1"] = "Tomador", ["C2"] = "Outro" }),
            new FakeItemService(new Dictionary<string, string> { ["SOJA"] = "SOJA", ["MILHO"] = "MILHO" }),
            TestConfiguration.Erp(erp), new FreightOrdersRecalculateService(db));
        return (db, svc);
    }

    [Fact]
    public async Task Create_AssignsCode_StatusOpen()
    {
        var (_, svc) = Arrange();
        var o = await svc.SaveAsync(Model(), "u");
        Assert.Equal("ST-0001", o.Code);
        Assert.Equal(FreightOrderStatus.Open, o.Status);
        Assert.Equal("u", o.CreatedBy);
    }

    [Fact]
    public async Task Create_SnapshotsPartnerMunicipalityAndItemNames_ItemNameFromLookup()
    {
        var db = TestDb.CreateUnitOfWork();
        db.Context.Municipalities.Add(new Municipality { Code = "3550308", Name = "Itaberá", StateAbbreviation = "SP" });
        db.Context.Municipalities.Add(new Municipality { Code = "4106902", Name = "Paranaguá", StateAbbreviation = "PR" });
        await db.SaveChangesAsync();
        var svc = new FreightOrdersSaveService(db, new FakeDocNumberSequenceService(),
            new FakeBusinessPartnerService(new Dictionary<string, string> { ["C1"] = "Tomador", ["C2"] = "Remetente" }),
            new FakeItemService(new Dictionary<string, string> { ["SOJA"] = "SOJA EM GRAOS" }),
            TestConfiguration.Erp("STANDALONE"), new FreightOrdersRecalculateService(db));

        var o = await svc.SaveAsync(Model(m => { m.SenderCardCode = "C2"; m.ItemName = "ADULTERADO"; }), "u");

        Assert.Equal("Tomador", o.TakerName);
        Assert.Equal("Remetente", o.SenderName);
        Assert.Null(o.RecipientName);
        Assert.Equal("Itaberá", o.StartMunicipalityName);
        Assert.Equal("Paranaguá", o.EndMunicipalityName);
        Assert.Equal("SOJA EM GRAOS", o.ItemName);
    }

    private sealed class NoSeriesDocNumbers : DocNumberSequenceService
    {
        public NoSeriesDocNumbers() : base(null!) { }
        public override Task<DocNumberAssignment> AssignAsync(DocNumberRequest request) =>
            throw new NotFoundException("Key FreightOrder not found.");
    }

    [Fact]
    public async Task Create_WithoutDefaultSeries_IsBusinessError_NotNotFound()
    {
        var db = TestDb.CreateUnitOfWork();
        var svc = new FreightOrdersSaveService(db, new NoSeriesDocNumbers(),
            new FakeBusinessPartnerService(new Dictionary<string, string> { ["C1"] = "Tomador" }),
            new FakeItemService(new Dictionary<string, string> { ["SOJA"] = "SOJA" }),
            TestConfiguration.Erp("STANDALONE"), new FreightOrdersRecalculateService(db));

        var ex = await Assert.ThrowsAsync<DefaultException>(() => svc.SaveAsync(Model(), "u"));
        Assert.Contains("série de numeração", ex.Message);
    }

    [Fact]
    public void FreightDocNumberMigration_SeedsAutomaticDefaultSeriesForBothCodes()
    {
        var path = Directory.GetFiles(FindMigrations(), "*_AddFreightDocNumbers.cs").Single();
        var sql = File.ReadAllText(path);
        Assert.Contains("19", sql);
        Assert.Contains("20", sql);
        Assert.Contains("[Default]", sql);
    }

    private static string FindMigrations()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "SiagroB1.Migrations")))
            dir = dir.Parent;
        return Path.Combine(dir!.FullName, "SiagroB1.Migrations", "AppContext");
    }

    [Fact]
    public async Task ContractedWeight_MustBePositive()
    {
        var (_, svc) = Arrange();
        var ex = await Assert.ThrowsAsync<DefaultException>(() => svc.SaveAsync(Model(m => m.ContractedWeightKg = 0), "u"));
        Assert.Equal("Informe o volume contratado.", ex.Message);
    }

    [Fact]
    public async Task UnitPrice_MustBePositive()
    {
        var (_, svc) = Arrange();
        await Assert.ThrowsAsync<DefaultException>(() => svc.SaveAsync(Model(m => m.UnitPrice = 0), "u"));
    }

    [Fact]
    public async Task ValidTo_BeforeValidFrom_Rejected()
    {
        var (_, svc) = Arrange();
        await Assert.ThrowsAsync<DefaultException>(() =>
            svc.SaveAsync(Model(m => m.ValidTo = m.ValidFrom.AddDays(-1)), "u"));
    }

    private static async Task<(UnitOfWork Db, FreightOrdersSaveService Svc, FreightOrder Order)> WithCte()
    {
        var (db, svc) = Arrange();
        var o = await svc.SaveAsync(Model(), "u");
        db.Context.TransportDocuments.Add(new TransportDocument { BranchCode = "01", FreightOrderKey = o.Key, Status = CteStatus.Authorized });
        await db.SaveChangesAsync();
        return (db, svc, o);
    }

    [Fact]
    public async Task Update_WithAuthorizedCte_CannotChangeTakerOrItem()
    {
        var (_, svc, o) = await WithCte();
        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            svc.SaveAsync(Model(m => { m.Key = o.Key; m.TakerCardCode = "C2"; }), "u"));
        Assert.Equal("Ordem com CT-e autorizado: tomador e produto não podem ser alterados.", ex.Message);
        await Assert.ThrowsAsync<DefaultException>(() =>
            svc.SaveAsync(Model(m => { m.Key = o.Key; m.ItemCode = "MILHO"; m.ItemName = "MILHO"; }), "u"));
    }

    [Fact]
    public async Task Update_WithAuthorizedCte_CanChangePriceAndObservation()
    {
        var (_, svc, o) = await WithCte();
        var r = await svc.SaveAsync(Model(m => { m.Key = o.Key; m.UnitPrice = 120m; m.Observation = "obs"; }), "u");
        Assert.Equal(120m, r.UnitPrice);
        Assert.Equal("obs", r.Observation);
    }

    [Fact]
    public async Task Update_LoweringContractedWeight_RecalculatesStatus()
    {
        var (db, svc) = Arrange();
        var o = await svc.SaveAsync(Model(), "u");
        var cte = new TransportDocument { BranchCode = "01", FreightOrderKey = o.Key, Status = CteStatus.Authorized };
        cte.Quantities.Add(new TransportDocumentQuantity { UnitCode = "01", MeasureType = "PESO BRUTO", Quantity = 20_000m });
        db.Context.TransportDocuments.Add(cte);
        o.Status = FreightOrderStatus.InExecution;
        o.TransportedWeightKg = 20_000m;
        await db.SaveChangesAsync();

        var r = await svc.SaveAsync(Model(m => { m.Key = o.Key; m.ContractedWeightKg = 20_000m; }), "u");

        Assert.Equal(FreightOrderStatus.Completed, r.Status);
    }

    [Theory]
    [InlineData(FreightOrderStatus.Completed)]
    [InlineData(FreightOrderStatus.Cancelled)]
    public async Task Update_CompletedOrCancelled_Rejected(FreightOrderStatus status)
    {
        var (db, svc) = Arrange();
        var o = await svc.SaveAsync(Model(), "u");
        o.Status = status;
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<DefaultException>(() => svc.SaveAsync(Model(m => m.Key = o.Key), "u"));
    }

    [Fact]
    public async Task Sapb1Mode_Rejected()
    {
        var (_, svc) = Arrange("SAPB1");
        var ex = await Assert.ThrowsAsync<DefaultException>(() => svc.SaveAsync(Model(), "u"));
        Assert.Equal(SiagroB1.Application.Services.Cte.BranchCteSettingsService.StandaloneOnly, ex.Message);
    }
}
