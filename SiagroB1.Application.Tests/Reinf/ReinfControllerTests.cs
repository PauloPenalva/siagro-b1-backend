using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OData.Formatter;
using SiagroB1.Application.Services.Reinf;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Fiscal.Reinf;
using SiagroB1.Infra.Context;
using SiagroB1.Web.Controllers;

namespace SiagroB1.Application.Tests.Reinf;

public class ReinfControllerTests
{
    private const string Root = "12345678";

    private sealed class FixedTime : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 10, 24, 12, 0, 0, TimeSpan.Zero);
    }

    private sealed class StubService(ReinfPeriodOverview overview) : IReinfService
    {
        public Task<ReinfPeriodOverview> GetOverviewAsync(string taxIdRoot, string period, CancellationToken ct) => Task.FromResult(overview);
        public Task<IReadOnlyList<Guid>> TransmitAsync(string taxIdRoot, string period, string user, CancellationToken ct) => throw new NotImplementedException();
        public Task QueryBatchAsync(Guid batchKey, CancellationToken ct) => Task.CompletedTask;
        public Task<Guid> SendRegistrationAsync(string taxIdRoot, string user, CancellationToken ct) => throw new NotImplementedException();
        public Task<Guid> CloseAsync(string taxIdRoot, string period, string user, CancellationToken ct) => throw new NotImplementedException();
        public Task<Guid> ReopenAsync(string taxIdRoot, string period, string user, CancellationToken ct) => throw new NotImplementedException();
        public Task<int> ReleaseUnconfirmedAsync(string taxIdRoot, string period, CancellationToken ct) => throw new NotImplementedException();
    }

    private static ReinfController Controller(AppDbContext ctx, IReinfService service, string erp = "STANDALONE") =>
        new(service, new ReinfSettingsService(ctx, new FixedTime()), ctx, TestConfiguration.Erp(erp))
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

    private static ReinfService RealService(AppDbContext ctx) => new(
        ctx, TestConfiguration.Erp("STANDALONE"), new ReinfAcquisitionCalculator(ctx), new FakeReinfClient(),
        new FakeReinfCertificateProvider(), new ReinfEventIdGenerator(), new ReinfSchemaValidator(), new ReinfAppInfo("1.0"), new FixedTime());

    private static string Bad(IActionResult result) => Assert.IsType<Microsoft.AspNetCore.OData.Results.BadRequestODataResult>(result).Error.Message;

    private static ODataActionParameters Params(string period) => new() { ["TaxIdRoot"] = Root, ["Period"] = period };

    private static AppDbContext SeededContext(ReinfPeriodStatus? status = null)
    {
        var ctx = TestDb.CreateUnitOfWork().Context;
        ctx.Branchs.Add(new Branch { Code = "01", BranchName = "Matriz", TaxId = "12345678000195" });
        ctx.ReinfSettings.Add(new ReinfSettings
        {
            TaxIdRoot = Root, BranchCode = "01", RegistrationStatus = ReinfRegistrationStatus.Accepted,
            TaxClassification = "99", ContactName = "Fulano", ContactCpf = "12345678909", ValidFrom = "2026-01",
        });
        if (status is not null) ctx.ReinfPeriods.Add(new ReinfPeriod { TaxIdRoot = Root, Period = "2026-09", Status = status.Value });
        ctx.SaveChanges();
        return ctx;
    }

    [Fact]
    public async Task Transmit_InvalidPeriod_ReturnsBadRequest()
    {
        var ctx = SeededContext();
        var bad = Bad(await Controller(ctx, RealService(ctx)).ReinfTransmit(Params("2026-13")));
        Assert.Equal("Período inválido (use AAAA-MM).", bad);
    }

    [Fact]
    public async Task Transmit_ClosedPeriod_ReturnsServiceMessage()
    {
        var ctx = SeededContext(ReinfPeriodStatus.Closed);
        var bad = Bad(await Controller(ctx, RealService(ctx)).ReinfTransmit(Params("2026-09")));
        Assert.Equal(ReinfService.PeriodClosedMessage, bad);
    }

    [Fact]
    public async Task Transmit_NullParameters_ReturnsBadRequest()
    {
        var ctx = SeededContext();
        Bad(await Controller(ctx, RealService(ctx)).ReinfTransmit(null));
    }

    [Fact]
    public async Task Overview_ReturnsFlatRows()
    {
        var ctx = SeededContext();
        var row = new ReinfAcquisitionRow("12345678000195", "01", "P1", "Produtor 1", 2, "11100000001", true, [], 1000m, 12m, 1m, 2m, "h");
        var overview = new ReinfPeriodOverview(Root, "2026-09", ReinfPeriodStatus.Open,
            [new ReinfOverviewRow(row, Guid.NewGuid(), "Novo", null, null)], [new ReinfIssue("Pendência", "P2")]);

        var ok = Assert.IsType<OkObjectResult>(await Controller(ctx, new StubService(overview)).ReinfOverview(Root, "2026-09"));

        var dto = Assert.IsType<ReinfOverviewDto>(ok.Value);
        Assert.Equal("Open", dto.PeriodStatus);
        var r = Assert.Single(dto.Rows);
        Assert.Equal("Produtor 1", r.CardName);
        Assert.Equal(1000m, r.Gross);
        Assert.Equal("Novo", r.Situation);
        Assert.Equal("Pendência", Assert.Single(dto.Issues).Message);
    }

    [Fact]
    public async Task Overview_OrphanRowWithoutAcquisition_IsListed()
    {
        var ctx = SeededContext();
        var overview = new ReinfPeriodOverview(Root, "2026-09", ReinfPeriodStatus.Open,
            [new ReinfOverviewRow(null, Guid.NewGuid(), "Excluir", "REC1", null)], []);

        var ok = Assert.IsType<OkObjectResult>(await Controller(ctx, new StubService(overview)).ReinfOverview(Root, "2026-09"));

        var r = Assert.Single(Assert.IsType<ReinfOverviewDto>(ok.Value).Rows);
        Assert.Equal("Excluir", r.Situation);
        Assert.Equal("REC1", r.Receipt);
    }

    [Fact]
    public async Task NonStandalone_ReturnsBadRequest()
    {
        var ctx = SeededContext();
        var c = Controller(ctx, RealService(ctx), "SAPB1");
        Bad(await c.ReinfContributors());
        Bad(await c.ReinfSettingsGet(Root));
    }

    [Fact]
    public async Task Contributors_ListsRootsWithHeadquartersName()
    {
        var ctx = SeededContext();
        ctx.Branchs.Add(new Branch { Code = "02", BranchName = "Filial", TaxId = "12345678000276" });
        ctx.Branchs.Add(new Branch { Code = "03", BranchName = "Outra Empresa", TaxId = "99999999000100" });
        ctx.SaveChanges();

        var ok = Assert.IsType<OkObjectResult>(await Controller(ctx, RealService(ctx)).ReinfContributors());

        var list = Assert.IsAssignableFrom<IEnumerable<ReinfContributorDto>>(ok.Value).ToList();
        Assert.Equal(2, list.Count);
        var first = list.Single(x => x.TaxIdRoot == Root);
        Assert.Equal("Matriz", first.Name);
        Assert.True(first.HasSettings);
        Assert.False(list.Single(x => x.TaxIdRoot == "99999999").HasSettings);
    }

    [Fact]
    public async Task Events_ReturnsPeriodEventsWithBatchData()
    {
        var ctx = SeededContext();
        var batch = new ReinfBatch { TaxIdRoot = Root, Protocol = "PROT1", Status = ReinfBatchStatus.Sent, RequestXml = "<req/>", ResponseXml = "<resp/>" };
        ctx.ReinfBatches.Add(batch);
        ctx.ReinfEvents.Add(new ReinfEvent { TaxIdRoot = Root, Period = "2026-09", Type = ReinfEventType.R2055, EventId = "ID1", Xml = "<x/>", BatchKey = batch.Key, Receipt = "REC" });
        ctx.ReinfEvents.Add(new ReinfEvent { TaxIdRoot = Root, Period = "2026-08", Type = ReinfEventType.R2055, EventId = "ID2", Xml = "<y/>" });
        ctx.ReinfEvents.Add(new ReinfEvent { TaxIdRoot = Root, Period = null, Type = ReinfEventType.R1000, EventId = "ID3", Xml = "<z/>" });
        ctx.SaveChanges();
        var c = Controller(ctx, RealService(ctx));

        var period = Assert.IsAssignableFrom<IEnumerable<ReinfEventDto>>(Assert.IsType<OkObjectResult>(await c.ReinfEvents(Root, "2026-09")).Value).ToList();
        var registration = Assert.IsAssignableFrom<IEnumerable<ReinfEventDto>>(Assert.IsType<OkObjectResult>(await c.ReinfEvents(Root, null)).Value).ToList();

        var e = Assert.Single(period);
        Assert.Equal("R2055", e.Type);
        Assert.Equal("PROT1", e.BatchProtocol);
        Assert.Equal("REC", e.Receipt);
        Assert.Equal("ID3", Assert.Single(registration).EventId);

        var xml = Assert.IsType<ReinfBatchXmlDto>(Assert.IsType<OkObjectResult>(await c.ReinfBatchXml(batch.Key)).Value);
        Assert.Equal("<req/>", xml.RequestXml);
        Assert.Equal("<resp/>", xml.ResponseXml);
    }

    [Fact]
    public async Task Events_InvalidPeriod_ReturnsBadRequest()
    {
        var ctx = SeededContext();
        var bad = Bad(await Controller(ctx, RealService(ctx)).ReinfEvents(Root, "2026-00"));
        Assert.Equal("Período inválido (use AAAA-MM).", bad);
    }

    [Fact]
    public async Task SettingsGet_UnknownRoot_ReturnsDefaultsNotExisting()
    {
        var ctx = SeededContext();
        var ok = Assert.IsType<OkObjectResult>(await Controller(ctx, RealService(ctx)).ReinfSettingsGet("87654321"));
        var dto = Assert.IsType<ReinfSettingsDto>(ok.Value);
        Assert.False(dto.Exists);
        Assert.Equal("87654321", dto.TaxIdRoot);
    }

    [Fact]
    public async Task SettingsSave_InvalidCpf_ReturnsBusinessMessage_AndNullParametersRejected()
    {
        var ctx = SeededContext();
        var c = Controller(ctx, RealService(ctx));
        Bad(await c.ReinfSettingsSave(null));
        var bad = Bad(await c.ReinfSettingsSave(new ODataActionParameters
        {
            ["TaxIdRoot"] = Root, ["BranchCode"] = "01", ["ContactCpf"] = "123", ["ContactName"] = "X",
            ["TaxClassification"] = "99", ["ValidFrom"] = "2026-01",
        }));
        Assert.Equal("CPF do contato deve ter 11 dígitos.", bad);
    }
}
