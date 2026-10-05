using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.Nfe;
using SiagroB1.Application.Services.SalesInvoices;
using SiagroB1.Application.Services.Taxes;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Fiscal.Nfe;

namespace SiagroB1.Application.Tests.Nfe;

/// <summary>"Consultar situação" (spec §9.3): o caminho de saída do "sem resposta".</summary>
public class SalesInvoicesNfeConsultServiceTests
{
    private static (SalesInvoicesNfeIssueService Issue, SalesInvoicesNfeConsultService Consult) Services(
        NfeScenario scenario, FakeNfeSefazClient sefaz, SalesInvoicesConfirmService confirm,
        FakeNfeNumberReservationService? reservation = null)
    {
        reservation ??= new FakeNfeNumberReservationService();
        var config = NfeTestSeed.Config();
        var options = new NfeOptions(config);
        var settings = new BranchNfeSettingsService(scenario.Db, options, sefaz);
        var handler = new SalesInvoiceNfeResultHandler(scenario.Db, confirm, NullLogger<SalesInvoiceNfeResultHandler>.Instance);

        return (
            new SalesInvoicesNfeIssueService(scenario.Db, new TaxCalculationGate(scenario.Db, config),
                new NfeReadinessValidator(scenario.Db, options), settings, reservation, sefaz, handler, options,
                NullLogger<SalesInvoicesNfeIssueService>.Instance, NfeTestSeed.Clock),
            new SalesInvoicesNfeConsultService(
                scenario.Db, settings, sefaz, handler, reservation, NullLogger<SalesInvoicesNfeConsultService>.Instance));
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
        var sefaz = new FakeNfeSefazClient();
        var (_, consult) = Services(scenario, sefaz, new RecordingConfirmService(scenario.Db));

        // Com o XML assinado gravado: só a trava de situação (P12) pode recusar.
        var invoice = await scenario.Db.Context.SalesInvoices.SingleAsync();
        invoice.NfeStatus = NfeStatus.Rejected;
        scenario.Db.Context.SalesInvoiceNfeXmls.Add(new SalesInvoiceNfeXml
        {
            Key = Guid.NewGuid(), SalesInvoiceKey = invoice.Key, Kind = SalesInvoiceNfeXmlKind.Signed,
            Xml = "<NFe/>", CreatedAt = DateTime.Now,
        });
        await scenario.Db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<DefaultException>(() => consult.ExecuteAsync(scenario.InvoiceKey, "tester"));

        Assert.Equal("Só a NF-e em processamento é consultada.", ex.Message);
        Assert.Empty(sefaz.Consulted);
    }

    [Fact]
    public async Task Consult_while_an_emission_is_in_progress_is_refused_without_calling_sefaz()
    {
        var scenario = await NfeTestSeed.SeedAsync();
        var (sefaz, _, _) = await ProcessingAsync(scenario);
        var reservation = new FakeNfeNumberReservationService { EmissionBusy = true };
        var (_, consult) = Services(scenario, sefaz, new RecordingConfirmService(scenario.Db), reservation);

        var ex = await Assert.ThrowsAsync<DefaultException>(() => consult.ExecuteAsync(scenario.InvoiceKey, "tester"));

        Assert.Equal("A emissão deste documento já está em andamento. Aguarde e consulte a situação.", ex.Message);
        Assert.Empty(sefaz.Consulted);
    }

    [Fact]
    public async Task Communication_failure_keeps_processing_and_clears_the_stale_status_code()
    {
        var scenario = await NfeTestSeed.SeedAsync();
        var (sefaz, _, consult) = await ProcessingAsync(scenario);
        (await scenario.Db.Context.SalesInvoices.SingleAsync()).NfeStatusCode = "656";
        await scenario.Db.SaveChangesAsync();
        sefaz.ConsultResponses.Enqueue(_ => throw new NfeCommunicationException("Tempo esgotado."));

        var outcome = await consult.ExecuteAsync(scenario.InvoiceKey, "tester");

        Assert.Equal(NfeStatus.Processing, outcome.NfeStatus);
        Assert.Null(outcome.StatusCode);
        var saved = await scenario.Db.Context.SalesInvoices.AsNoTracking().SingleAsync();
        Assert.Null(saved.NfeStatusCode);
        Assert.StartsWith("Sem resposta da SEFAZ na consulta", saved.NfeStatusReason);
        Assert.DoesNotContain("detalhe técnico", saved.NfeStatusReason);
    }

    [Fact]
    public async Task Unexpected_failure_keeps_processing_with_the_technical_detail()
    {
        var scenario = await NfeTestSeed.SeedAsync();
        var (sefaz, _, consult) = await ProcessingAsync(scenario);
        (await scenario.Db.Context.SalesInvoices.SingleAsync()).NfeStatusCode = "656";
        await scenario.Db.SaveChangesAsync();
        sefaz.ConsultResponses.Enqueue(_ => throw new InvalidOperationException("XML ilegível"));

        var outcome = await consult.ExecuteAsync(scenario.InvoiceKey, "tester");

        Assert.Equal(NfeStatus.Processing, outcome.NfeStatus);
        var saved = await scenario.Db.Context.SalesInvoices.AsNoTracking().SingleAsync();
        Assert.Null(saved.NfeStatusCode);
        Assert.Contains("(detalhe técnico: XML ilegível)", saved.NfeStatusReason);
    }
}
