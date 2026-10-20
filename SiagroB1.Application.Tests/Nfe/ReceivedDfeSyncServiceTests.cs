using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.Nfe;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Fiscal.Nfe;

namespace SiagroB1.Application.Tests.Nfe;

/// <summary>Sincronização dos DF-e emitidos contra a filial (spec 2026-10-20, T3).</summary>
public class ReceivedDfeSyncServiceTests
{
    private const string Key1 = "35261011222333000181550010000000011481516231";
    private const string Key2 = "35261011222333000181550010000000021481516232";

    private static readonly DateTimeOffset Now = new(2026, 10, 20, 15, 0, 0, TimeSpan.FromHours(-3));

    private static DateTimeOffset At(int day, int hour, int minute = 0) => new(2026, 10, day, hour, minute, 0, TimeSpan.FromHours(-3));

    private static ReceivedDfeSyncService Service(NfeScenario s, FakeNfeSefazClient sefaz, DateTimeOffset? now = null) =>
        new(s.Db, new BranchNfeSettingsService(s.Db, new NfeOptions(NfeTestSeed.Config()), sefaz), sefaz,
            NullLogger<ReceivedDfeSyncService>.Instance, () => now ?? Now);

    private static string Summary(string key, int situation = 1) =>
        $"<resNFe xmlns=\"http://www.portalfiscal.inf.br/nfe\"><chNFe>{key}</chNFe><CNPJ>11222333000181</CNPJ><xNome>FORNECEDOR LTDA</xNome>" +
        $"<dhEmi>2026-10-15T10:00:00-03:00</dhEmi><vNF>1500.50</vNF><cSitNFe>{situation}</cSitNFe></resNFe>";

    private static string Full(string key) =>
        $"<nfeProc xmlns=\"http://www.portalfiscal.inf.br/nfe\"><NFe><infNFe Id=\"NFe{key}\"><ide><dhEmi>2026-10-15T10:00:00-03:00</dhEmi></ide>" +
        "<emit><CNPJ>11222333000181</CNPJ><xNome>FORNECEDOR LTDA</xNome></emit><total><ICMSTot><vNF>1500.50</vNF></ICMSTot></total></infNFe></NFe></nfeProc>";

    private static string Event(string key, int type) =>
        $"<resEvento xmlns=\"http://www.portalfiscal.inf.br/nfe\"><chNFe>{key}</chNFe><dhEvento>2026-10-16T10:00:00-03:00</dhEvento><tpEvento>{type}</tpEvento></resEvento>";

    private static NfeDistributionResult Batch(int status, long last, long max, params NfeDistributionDocument[] docs) =>
        new(status, "msg", last, max, docs);

    [Fact]
    public void IsDue_BeforeFirstTime_False() =>
        Assert.False(DfeSchedule.IsDue("12:00;20:00", null, At(20, 11, 59)));

    [Fact]
    public void IsDue_AfterTime_NotYetRun_True() =>
        Assert.True(DfeSchedule.IsDue("12:00;20:00", At(19, 20, 1), At(20, 12, 5)));

    [Fact]
    public void IsDue_AlreadyRunAfterTime_False() =>
        Assert.False(DfeSchedule.IsDue("12:00;20:00", At(20, 12, 1), At(20, 13)));

    [Fact]
    public void IsDue_MissedTwoTimes_RunsOnce()
    {
        var run = At(19, 20, 1);
        Assert.True(DfeSchedule.IsDue("08:00;12:00;20:00", run, At(20, 13)));
        Assert.False(DfeSchedule.IsDue("08:00;12:00;20:00", At(20, 13), At(20, 13, 5)));
    }

    [Fact]
    public void IsDue_EmptySchedule_False()
    {
        Assert.False(DfeSchedule.IsDue(null, null, Now));
        Assert.False(DfeSchedule.IsDue("", null, Now));
    }

    [Fact]
    public async Task SetSchedule_LessThanOneHourApart_IsRejected()
    {
        var s = await NfeTestSeed.SeedAsync();

        var e = await Assert.ThrowsAsync<DefaultException>(() =>
            Service(s, new FakeNfeSefazClient()).SetScheduleAsync("01", ["12:00", "12:30"]));

        Assert.Equal("Os horários precisam ter pelo menos 1 hora de intervalo.", e.Message);
    }

    [Fact]
    public async Task SetSchedule_ValidTimes_AreSortedAndSaved()
    {
        var s = await NfeTestSeed.SeedAsync();

        var state = await Service(s, new FakeNfeSefazClient()).SetScheduleAsync("01", ["20:00", "08:00"]);

        Assert.Equal("08:00;20:00", state.ScheduleTimes);
        await Assert.ThrowsAsync<DefaultException>(() => Service(s, new FakeNfeSefazClient()).SetScheduleAsync("01", ["25:00"]));
        await Assert.ThrowsAsync<DefaultException>(() =>
            Service(s, new FakeNfeSefazClient()).SetScheduleAsync("01", ["01:00", "03:00", "05:00", "07:00", "09:00", "11:00", "13:00"]));
    }

    [Fact]
    public async Task RunScheduled_OnlyDueBranches_CallSefaz()
    {
        var s = await NfeTestSeed.SeedAsync();
        s.Db.Context.Branchs.Add(new Branch
        {
            Code = "02", BranchName = "Outra", ShortName = "Outra", TaxId = "99888777000166", StateCode = "SP",
        });
        s.Db.Context.BranchNfeSettings.Add(new BranchNfeSettings
        {
            BranchCode = "02", Environment = NfeEnvironment.Homologation, DefaultSeries = 1, CertificatePfx = [1],
            CertificatePasswordCipher = [1],
        });
        s.Db.Context.BranchDfeSyncStates.AddRange(
            new BranchDfeSyncState { BranchCode = "01", ScheduleTimes = "12:00", LastScheduledRunAt = At(19, 12, 1) },
            new BranchDfeSyncState { BranchCode = "02", ScheduleTimes = "20:00", LastScheduledRunAt = At(19, 20, 1) });
        await s.Db.SaveChangesAsync();
        var sefaz = new FakeNfeSefazClient();
        sefaz.EnqueueDistribution(Batch(137, 0, 0));

        await Service(s, sefaz).RunScheduledAsync(Now);

        Assert.Single(sefaz.DistributionByNsuCalls);
        Assert.Equal("12345678000195", sefaz.DistributionByNsuCalls[0].RecipientTaxId);
        var state = await s.Db.Context.BranchDfeSyncStates.AsNoTracking().SingleAsync(x => x.BranchCode == "01");
        Assert.Equal(Now, state.LastScheduledRunAt);
        Assert.False(DfeSchedule.IsDue("12:00", state.LastScheduledRunAt, Now));
    }

    [Fact]
    public async Task RunScheduled_InsideWaitWindow_SkipsAndMarksAsRun()
    {
        var s = await NfeTestSeed.SeedAsync();
        s.Db.Context.BranchDfeSyncStates.Add(new BranchDfeSyncState
        {
            BranchCode = "01", ScheduleTimes = "12:00", NextQueryAllowedAt = Now.AddMinutes(30),
        });
        await s.Db.SaveChangesAsync();
        var sefaz = new FakeNfeSefazClient();

        await Service(s, sefaz).RunScheduledAsync(Now);

        Assert.Empty(sefaz.DistributionByNsuCalls);
        var state = await s.Db.Context.BranchDfeSyncStates.AsNoTracking().SingleAsync();
        Assert.Equal(Now, state.LastScheduledRunAt);
    }

    [Fact]
    public async Task Sync_InsertsSummaries_AndAdvancesNsu()
    {
        var s = await NfeTestSeed.SeedAsync();
        var sefaz = new FakeNfeSefazClient();
        sefaz.EnqueueDistribution(Batch(138, 2, 5,
            new NfeDistributionDocument(1, "resNFe_v1.01.xsd", Summary(Key1)),
            new NfeDistributionDocument(2, "resNFe_v1.01.xsd", Summary(Key2))));
        sefaz.EnqueueDistribution(Batch(138, 5, 5));

        var result = await Service(s, sefaz).SyncBranchAsync("01");

        Assert.Equal(2, result.NewDocuments);
        Assert.Equal(2, sefaz.DistributionByNsuCalls.Count);
        Assert.Equal(2, sefaz.DistributionByNsuCalls[1].LastNsu);
        var rows = await s.Db.Context.ReceivedDfes.AsNoTracking().OrderBy(x => x.Nsu).ToListAsync();
        Assert.Equal([Key1, Key2], rows.Select(x => x.AccessKey));
        Assert.Equal("FORNECEDOR LTDA", rows[0].IssuerName);
        Assert.Equal(1500.50m, rows[0].TotalValue);
        Assert.Null(rows[0].Xml);
        var state = await s.Db.Context.BranchDfeSyncStates.AsNoTracking().SingleAsync();
        Assert.Equal(5, state.LastNsu);
        Assert.Equal(5, state.MaxNsu);
        Assert.Equal(Now.AddHours(1), state.NextQueryAllowedAt);
    }

    [Fact]
    public async Task Sync_SameKeyTwice_UpdatesInsteadOfDuplicating()
    {
        var s = await NfeTestSeed.SeedAsync();
        var sefaz = new FakeNfeSefazClient();
        sefaz.EnqueueDistribution(Batch(138, 1, 1, new NfeDistributionDocument(1, "resNFe_v1.01.xsd", Summary(Key1))));
        await Service(s, sefaz).SyncBranchAsync("01");

        var later = Now.AddHours(2);
        sefaz.EnqueueDistribution(Batch(138, 2, 2, new NfeDistributionDocument(2, "procNFe_v4.00.xsd", Full(Key1))));
        var result = await Service(s, sefaz, later).SyncBranchAsync("01");

        Assert.Equal(0, result.NewDocuments);
        Assert.Equal(1, result.UpdatedDocuments);
        var row = await s.Db.Context.ReceivedDfes.AsNoTracking().SingleAsync();
        Assert.NotNull(row.Xml);
        Assert.Equal(later, row.XmlDownloadedAt);
    }

    [Fact]
    public async Task Sync_CancellationEvent_MarksCancelled()
    {
        var s = await NfeTestSeed.SeedAsync();
        var sefaz = new FakeNfeSefazClient();
        sefaz.EnqueueDistribution(Batch(138, 2, 2,
            new NfeDistributionDocument(1, "resNFe_v1.01.xsd", Summary(Key1)),
            new NfeDistributionDocument(2, "resEvento_v1.01.xsd", Event(Key1, 110111))));

        await Service(s, sefaz).SyncBranchAsync("01");

        var row = await s.Db.Context.ReceivedDfes.AsNoTracking().SingleAsync();
        Assert.Equal(DfeInvoiceSituation.Cancelled, row.Situation);
    }

    [Fact]
    public async Task Sync_ManifestationEventFromSefaz_UpdatesStatusOnlyForward()
    {
        var s = await NfeTestSeed.SeedAsync();
        var sefaz = new FakeNfeSefazClient();
        sefaz.EnqueueDistribution(Batch(138, 3, 3,
            new NfeDistributionDocument(1, "resNFe_v1.01.xsd", Summary(Key1)),
            new NfeDistributionDocument(2, "resEvento_v1.01.xsd", Event(Key1, 210200)),
            new NfeDistributionDocument(3, "resEvento_v1.01.xsd", Event(Key1, 210210))));

        await Service(s, sefaz).SyncBranchAsync("01");

        var row = await s.Db.Context.ReceivedDfes.AsNoTracking().SingleAsync();
        Assert.Equal(DfeManifestationStatus.Confirmed, row.ManifestationStatus);
    }

    [Fact]
    public async Task Sync_MalformedDocument_IsSkipped_AndNsuAdvances()
    {
        var s = await NfeTestSeed.SeedAsync();
        var sefaz = new FakeNfeSefazClient();
        sefaz.EnqueueDistribution(Batch(138, 2, 2,
            new NfeDistributionDocument(1, "resNFe_v1.01.xsd", "<resNFe><broken"),
            new NfeDistributionDocument(2, "resNFe_v1.01.xsd", Summary(Key2))));

        var result = await Service(s, sefaz).SyncBranchAsync("01");

        Assert.Equal(1, result.NewDocuments);
        var state = await s.Db.Context.BranchDfeSyncStates.AsNoTracking().SingleAsync();
        Assert.Equal(2, state.LastNsu);
    }

    [Fact]
    public async Task Sync_BeforeNextAllowedTime_DoesNotCallSefaz()
    {
        var s = await NfeTestSeed.SeedAsync();
        s.Db.Context.BranchDfeSyncStates.Add(new BranchDfeSyncState { BranchCode = "01", NextQueryAllowedAt = At(20, 16, 30) });
        await s.Db.SaveChangesAsync();
        var sefaz = new FakeNfeSefazClient();

        var result = await Service(s, sefaz).SyncBranchAsync("01");

        Assert.Empty(sefaz.DistributionByNsuCalls);
        Assert.Equal("Aguarde até 16:30 para consultar novamente.", result.Message);
        Assert.Equal(At(20, 16, 30), result.NextQueryAllowedAt);
    }

    [Fact]
    public async Task Sync_656_SetsOneHourWait()
    {
        var s = await NfeTestSeed.SeedAsync();
        var sefaz = new FakeNfeSefazClient();
        sefaz.EnqueueDistribution(Batch(656, 0, 0));

        var result = await Service(s, sefaz).SyncBranchAsync("01");

        Assert.Equal(Now.AddHours(1), result.NextQueryAllowedAt);
        var state = await s.Db.Context.BranchDfeSyncStates.AsNoTracking().SingleAsync();
        Assert.Equal(Now.AddHours(1), state.NextQueryAllowedAt);
    }

    [Fact]
    public async Task Sync_StopsAfterMaxNsuReached()
    {
        var s = await NfeTestSeed.SeedAsync();
        var sefaz = new FakeNfeSefazClient();
        sefaz.EnqueueDistribution(Batch(138, 1, 1, new NfeDistributionDocument(1, "resNFe_v1.01.xsd", Summary(Key1))));

        await Service(s, sefaz).SyncBranchAsync("01");

        Assert.Single(sefaz.DistributionByNsuCalls);
    }

    [Fact]
    public async Task Sync_StopsAtTwentyCallsPerRun()
    {
        var s = await NfeTestSeed.SeedAsync();
        var sefaz = new FakeNfeSefazClient();
        for (var i = 1; i <= 25; i++)
            sefaz.EnqueueDistribution(Batch(138, i, 100));

        var result = await Service(s, sefaz).SyncBranchAsync("01");

        Assert.Equal(20, sefaz.DistributionByNsuCalls.Count);
        Assert.Null(result.NextQueryAllowedAt);
    }
}
