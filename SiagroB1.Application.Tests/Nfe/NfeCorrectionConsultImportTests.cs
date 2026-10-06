using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.Nfe;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Enums;
using static SiagroB1.Application.Tests.Support.NfeCancelTestServices;

namespace SiagroB1.Application.Tests.Nfe;

/// <summary>"Consultar situação" de NF-e autorizada importa as CC-e que faltam (spec 2026-10-06 §7.2, T5).</summary>
public class NfeCorrectionConsultImportTests
{
    private static SalesInvoicesNfeConsultService Consult(NfeScenario scenario, FakeNfeSefazClient sefaz) =>
        new(scenario.Db, new BranchNfeSettingsService(scenario.Db, new NfeOptions(NfeTestSeed.Config()), sefaz), sefaz,
            new SalesInvoiceNfeResultHandler(scenario.Db, new RecordingConfirmService(scenario.Db),
                NullLogger<SalesInvoiceNfeResultHandler>.Instance),
            SalesHandler(scenario.Db), new FakeNfeNumberReservationService(),
            NullLogger<SalesInvoicesNfeConsultService>.Instance);

    private static async Task<(NfeScenario Scenario, FakeNfeSefazClient Sefaz)> AuthorizedAsync()
    {
        var scenario = await NfeTestSeed.SeedAsync();
        await AuthorizeSaleAsync(scenario.Db, scenario.InvoiceKey);
        return (scenario, new FakeNfeSefazClient());
    }

    [Fact]
    public async Task Consult_of_authorized_nfe_imports_missing_corrections_once()
    {
        var (scenario, sefaz) = await AuthorizedAsync();
        for (var i = 0; i < 2; i++)
            sefaz.ConsultResponses.Enqueue(key => FakeNfeSefazClient.Authorized(key) with
            {
                Corrections = [FakeNfeSefazClient.CorrectionRegistered(1, "Primeira carta fora do Siagro"),
                    FakeNfeSefazClient.CorrectionRegistered(2, "Segunda carta fora do Siagro")],
            });

        var first = await Consult(scenario, sefaz).ExecuteAsync(scenario.InvoiceKey, "tester");
        var second = await Consult(scenario, sefaz).ExecuteAsync(scenario.InvoiceKey, "tester");

        Assert.Equal(2, first.ImportedCorrections);
        Assert.Equal(0, second.ImportedCorrections);
        var rows = await scenario.Db.Context.SalesInvoiceNfeCorrections.AsNoTracking().OrderBy(x => x.Sequence).ToListAsync();
        Assert.Equal([1, 2], rows.Select(x => x.Sequence));
        Assert.All(rows, r => Assert.Equal(NfeCorrectionImporter.ConsultUser, r.CreatedBy));
        Assert.Equal(NfeStatus.Authorized, first.NfeStatus);
    }

    [Fact]
    public async Task Consult_of_cancelled_nfe_saves_corrections_with_the_cancellation()
    {
        var (scenario, sefaz) = await AuthorizedAsync();
        sefaz.ConsultResponses.Enqueue(key => FakeNfeSefazClient.ConsultCancelled(key) with
        {
            Corrections = [FakeNfeSefazClient.CorrectionRegistered(1, "Carta antes do cancelamento")],
        });

        var outcome = await Consult(scenario, sefaz).ExecuteAsync(scenario.InvoiceKey, "tester");

        Assert.Equal(NfeStatus.Cancelled, outcome.NfeStatus);
        Assert.Equal(1, outcome.ImportedCorrections);
        Assert.True(await scenario.Db.Context.SalesInvoiceNfeCorrections.AnyAsync());
    }
}
