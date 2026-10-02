using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Nfe;
using SiagroB1.Application.Services.SalesInvoices;
using SiagroB1.Application.Services.Taxes;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Fiscal.Nfe;

namespace SiagroB1.Application.Tests.Nfe;

/// <summary>"Consultar situação" (spec §9.3): o caminho de saída do "sem resposta".</summary>
public class SalesInvoicesNfeConsultServiceTests
{
    private static (SalesInvoicesNfeIssueService Issue, SalesInvoicesNfeConsultService Consult) Services(
        NfeScenario scenario, FakeNfeSefazClient sefaz, SalesInvoicesConfirmService confirm)
    {
        var config = NfeTestSeed.Config();
        var options = new NfeOptions(config);
        var settings = new BranchNfeSettingsService(scenario.Db, options, sefaz);
        var handler = new SalesInvoiceNfeResultHandler(scenario.Db, confirm);

        return (
            new SalesInvoicesNfeIssueService(scenario.Db, new TaxCalculationGate(scenario.Db, config),
                new NfeReadinessValidator(scenario.Db, options), settings, new FakeNfeNumberReservationService(), sefaz, handler, options),
            new SalesInvoicesNfeConsultService(scenario.Db, settings, sefaz, handler));
    }

    /// <summary>Emite sem resposta: o documento fica em processamento com o XML assinado gravado.</summary>
    private static async Task<(FakeNfeSefazClient Sefaz, RecordingConfirmService Confirm, SalesInvoicesNfeConsultService Consult)> ProcessingAsync(NfeScenario scenario)
    {
        var sefaz = new FakeNfeSefazClient();
        sefaz.AuthorizeResponses.Enqueue(FakeNfeSefazClient.NoResponse);
        var confirm = new RecordingConfirmService(scenario.Db);
        var (issue, consult) = Services(scenario, sefaz, confirm);
        await issue.ExecuteAsync(scenario.InvoiceKey, "tester");
        return (sefaz, confirm, consult);
    }

    [Fact]
    public async Task Consult_after_no_response_authorizes_from_the_saved_signed_xml()
    {
        var scenario = await NfeTestSeed.SeedAsync();
        var (sefaz, confirm, consult) = await ProcessingAsync(scenario);
        sefaz.ConsultResponses.Enqueue(key => FakeNfeSefazClient.Authorized(key));

        var outcome = await consult.ExecuteAsync(scenario.InvoiceKey, "tester");

        Assert.Equal(NfeStatus.Authorized, outcome.NfeStatus);
        Assert.Equal(1, confirm.Calls);
        var signed = await scenario.Db.Context.SalesInvoiceNfeXmls.AsNoTracking().SingleAsync(x => x.Kind == SalesInvoiceNfeXmlKind.Signed);
        var proc = await scenario.Db.Context.SalesInvoiceNfeXmls.AsNoTracking().SingleAsync(x => x.Kind == SalesInvoiceNfeXmlKind.Authorized);
        Assert.Contains(signed.Xml[signed.Xml.IndexOf("<NFe", StringComparison.Ordinal)..].Trim(), proc.Xml);
    }

    [Fact]
    public async Task Not_found_at_sefaz_becomes_rejected_and_can_be_resent()
    {
        var scenario = await NfeTestSeed.SeedAsync();
        var (sefaz, _, consult) = await ProcessingAsync(scenario);
        sefaz.ConsultResponses.Enqueue(_ => new NfeSefazResult(217, "Rejeição: NF-e não consta na base de dados da SEFAZ"));

        var outcome = await consult.ExecuteAsync(scenario.InvoiceKey, "tester");

        Assert.Equal(NfeStatus.Rejected, outcome.NfeStatus);
    }

    [Fact]
    public async Task Other_answers_keep_processing_with_the_reason()
    {
        var scenario = await NfeTestSeed.SeedAsync();
        var (sefaz, _, consult) = await ProcessingAsync(scenario);
        sefaz.ConsultResponses.Enqueue(_ => new NfeSefazResult(656, "Rejeição: Consumo Indevido"));

        var outcome = await consult.ExecuteAsync(scenario.InvoiceKey, "tester");

        Assert.Equal(NfeStatus.Processing, outcome.NfeStatus);
        Assert.Equal("656", outcome.StatusCode);
    }

    [Fact]
    public async Task Only_processing_documents_are_consulted()
    {
        var scenario = await NfeTestSeed.SeedAsync();
        var (_, consult) = Services(scenario, new FakeNfeSefazClient(), new RecordingConfirmService(scenario.Db));

        await Assert.ThrowsAsync<DefaultException>(() => consult.ExecuteAsync(scenario.InvoiceKey, "tester"));
    }
}
