using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Cte;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Application.Tests.Cte;

public class TransportDocumentsCorrectionServiceTests : CteEventsTestBase
{
    private TransportDocumentsCorrectionService Service() =>
        new(Db, NfeTestSeed.Config(), Numbers, Contexts(), Sefaz, () => Clock);

    private static IReadOnlyList<(string, string, string)> Obs(string text = "NOVA OBSERVACAO") => [("compl", "xObs", text)];

    [Fact]
    public async Task Correction_outside_whitelist_is_rejected()
    {
        var key = await SeedAsync();

        var e = await Assert.ThrowsAsync<DefaultException>(
            () => Service().SendCorrectionAsync(key, [("vPrest", "vTPrest", "10.00")], "tester"));

        Assert.Equal("O campo vPrest/vTPrest não pode ser corrigido por CC-e.", e.Message);
        Assert.Equal(0, Sefaz.CorrectionCalls);
    }

    [Fact]
    public async Task Correction_value_over_max_length_is_rejected()
    {
        var key = await SeedAsync();

        await Assert.ThrowsAsync<DefaultException>(
            () => Service().SendCorrectionAsync(key, [("infCarga", "proPred", new string('A', 61))], "tester"));
    }

    [Fact]
    public async Task Correction_requires_authorized_status()
    {
        var key = await SeedAsync(CteStatus.Cancelled);

        await Assert.ThrowsAsync<DefaultException>(() => Service().SendCorrectionAsync(key, Obs(), "tester"));
    }

    [Fact]
    public async Task Correction_increments_sequence()
    {
        var key = await SeedAsync();
        Sefaz.CorrectionResponses.Enqueue(r => FakeCteSefazClient.CorrectionRegistered(r));
        Sefaz.CorrectionResponses.Enqueue(r => FakeCteSefazClient.CorrectionRegistered(r));

        await Service().SendCorrectionAsync(key, Obs("PRIMEIRA"), "tester");
        var second = await Service().SendCorrectionAsync(key, Obs("SEGUNDA"), "tester");

        Assert.Equal(135, second.StatusCode);
        Assert.Equal([1, 2], Sefaz.CorrectionRequests.Select(r => r.Sequence));
        var rows = await Db.Context.TransportDocumentCorrections.AsNoTracking().OrderBy(x => x.Sequence).ToListAsync();
        Assert.Equal([1, 2], rows.Select(r => r.Sequence));
        Assert.Contains("SEGUNDA", rows[1].ItemsJson);
        Assert.Equal(FakeCteSefazClient.EventProtocol, rows[1].Protocol);
        Assert.Equal(2, (await LoadAsync(key)).Xmls.Count(x => x.Kind == CteXmlKind.Correction));
        Assert.Equal(("compl", "xObs"), (Sefaz.CorrectionRequests[0].Items[0].Group, Sefaz.CorrectionRequests[0].Items[0].Field));
    }

    [Fact]
    public async Task Correction_refused_by_sefaz_does_not_consume_sequence()
    {
        var key = await SeedAsync();
        Sefaz.CorrectionResponses.Enqueue(_ => new SiagroB1.Fiscal.Nfe.NfeEventResult(494, "Rejeição"));
        Sefaz.CorrectionResponses.Enqueue(r => FakeCteSefazClient.CorrectionRegistered(r));

        await Service().SendCorrectionAsync(key, Obs(), "tester");
        await Service().SendCorrectionAsync(key, Obs(), "tester");

        Assert.Equal([1, 1], Sefaz.CorrectionRequests.Select(r => r.Sequence));
        Assert.Single(await Db.Context.TransportDocumentCorrections.ToListAsync());
    }

    [Fact]
    public async Task Correction_beyond_20_is_rejected()
    {
        var key = await SeedAsync();
        for (var i = 1; i <= 20; i++)
            Db.Context.TransportDocumentCorrections.Add(new SiagroB1.Domain.Entities.TransportDocumentCorrection
                { TransportDocumentKey = key, Sequence = i, SentAt = DateTime.Now });
        await Db.SaveChangesAsync();

        await Assert.ThrowsAsync<DefaultException>(() => Service().SendCorrectionAsync(key, Obs(), "tester"));
        Assert.Equal(0, Sefaz.CorrectionCalls);
    }
}
