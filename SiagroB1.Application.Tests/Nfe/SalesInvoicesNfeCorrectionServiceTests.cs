using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Fiscal.Nfe;
using static SiagroB1.Application.Tests.Support.NfeCancelTestServices;
using static SiagroB1.Application.Tests.Support.NfeCorrectionTestServices;

namespace SiagroB1.Application.Tests.Nfe;

/// <summary>CC-e do documento de saída (spec 2026-10-06 §7.1).</summary>
public class SalesInvoicesNfeCorrectionServiceTests
{
    private const string Text = "Onde se le placa ABC1D23, leia-se placa XYZ9K87.";

    private static async Task<(NfeScenario Scenario, FakeNfeSefazClient Sefaz)> AuthorizedAsync()
    {
        var scenario = await NfeTestSeed.SeedAsync();
        await AuthorizeSaleAsync(scenario.Db, scenario.InvoiceKey);
        return (scenario, new FakeNfeSefazClient());
    }

    private static void Registers(FakeNfeSefazClient sefaz) =>
        sefaz.CorrectionResponses.Enqueue(r => FakeNfeSefazClient.CorrectionRegistered(r.Sequence, r.Text));

    [Fact]
    public async Task First_correction_is_sequence_1_and_is_saved_with_the_event_xml()
    {
        var (scenario, sefaz) = await AuthorizedAsync();
        Registers(sefaz);

        var outcome = await SalesCorrection(scenario.Db, sefaz).ExecuteAsync(scenario.InvoiceKey, Text, "tester");

        Assert.Equal(1, outcome.Sequence);
        Assert.Equal(FakeNfeSefazClient.CorrectionProtocol(1), outcome.Protocol);
        var row = await scenario.Db.Context.SalesInvoiceNfeCorrections.AsNoTracking().SingleAsync();
        Assert.Equal(Text, row.Text);
        Assert.Equal("tester", row.CreatedBy);
        Assert.Contains("<procEventoNFe", row.ProcEventXml);
    }

    [Fact]
    public async Task Request_carries_key_sequence_issuer_and_brasilia_time()
    {
        var (scenario, sefaz) = await AuthorizedAsync();
        Registers(sefaz);

        await SalesCorrection(scenario.Db, sefaz).ExecuteAsync(scenario.InvoiceKey, Text, "tester");

        var request = Assert.Single(sefaz.CorrectionRequests);
        Assert.Equal(AccessKey, request.AccessKey);
        Assert.Equal(1, request.Sequence);
        Assert.Equal("12345678000195", request.IssuerDocument);
        Assert.Equal(TimeSpan.FromHours(-3), request.EventAt.Offset);
        Assert.Equal(NfeEnvironment.Homologation, Assert.Single(sefaz.CorrectionSettings).Environment);
    }

    [Fact]
    public async Task Second_correction_takes_the_next_sequence()
    {
        var (scenario, sefaz) = await AuthorizedAsync();
        Registers(sefaz);
        Registers(sefaz);
        var service = SalesCorrection(scenario.Db, sefaz);

        await service.ExecuteAsync(scenario.InvoiceKey, Text, "tester");
        var second = await service.ExecuteAsync(scenario.InvoiceKey, Text + " Corrige tambem o volume.", "tester");

        Assert.Equal(2, second.Sequence);
        Assert.Equal([1, 2], sefaz.CorrectionRequests.Select(r => r.Sequence));
    }

    [Fact]
    public async Task Correction_does_not_touch_the_document()
    {
        var (scenario, sefaz) = await AuthorizedAsync();
        Registers(sefaz);

        await SalesCorrection(scenario.Db, sefaz).ExecuteAsync(scenario.InvoiceKey, Text, "tester");

        var saved = await scenario.Db.Context.SalesInvoices.AsNoTracking().SingleAsync();
        Assert.Equal(NfeStatus.Authorized, saved.NfeStatus);
        Assert.Equal(InvoiceStatus.Confirmed, saved.InvoiceStatus);
        Assert.Equal("100", saved.NfeStatusCode);
        Assert.Equal("Autorizado o uso da NF-e", saved.NfeStatusReason);
    }

    [Fact]
    public async Task Text_is_normalized_before_sending_and_saving()
    {
        var (scenario, sefaz) = await AuthorizedAsync();
        Registers(sefaz);

        await SalesCorrection(scenario.Db, sefaz).ExecuteAsync(scenario.InvoiceKey, "  Onde se le “ABC”\r\nleia-se XYZ  ", "tester");

        Assert.Equal("Onde se le \"ABC\" leia-se XYZ", Assert.Single(sefaz.CorrectionRequests).Text);
        Assert.Equal("Onde se le \"ABC\" leia-se XYZ", (await scenario.Db.Context.SalesInvoiceNfeCorrections.AsNoTracking().SingleAsync()).Text);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   curta    \r\n ")]
    public async Task Short_text_is_refused_without_calling_sefaz(string? text)
    {
        var (scenario, sefaz) = await AuthorizedAsync();

        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            SalesCorrection(scenario.Db, sefaz).ExecuteAsync(scenario.InvoiceKey, text, "tester"));

        Assert.Equal("O texto da correção deve ter entre 15 e 1000 caracteres.", ex.Message);
        Assert.Empty(sefaz.CorrectionRequests);
    }

    [Fact]
    public async Task Long_text_is_refused()
    {
        var (scenario, sefaz) = await AuthorizedAsync();

        await Assert.ThrowsAsync<DefaultException>(() =>
            SalesCorrection(scenario.Db, sefaz).ExecuteAsync(scenario.InvoiceKey, new string('x', 1001), "tester"));

        Assert.Empty(sefaz.CorrectionRequests);
    }

    [Fact]
    public async Task Characters_the_sefaz_refuses_are_named()
    {
        var (scenario, sefaz) = await AuthorizedAsync();

        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            SalesCorrection(scenario.Db, sefaz).ExecuteAsync(scenario.InvoiceKey, "Valor em € corrigido ✓ aqui", "tester"));

        Assert.Equal("O texto da correção tem caracteres que a SEFAZ não aceita: € ✓", ex.Message);
        Assert.Empty(sefaz.CorrectionRequests);
    }

    [Theory]
    [InlineData(NfeStatus.None)]
    [InlineData(NfeStatus.Processing)]
    [InlineData(NfeStatus.Rejected)]
    [InlineData(NfeStatus.Cancelled)]
    public async Task Only_authorized_nfe_receives_a_correction(NfeStatus status)
    {
        var (scenario, sefaz) = await AuthorizedAsync();
        (await scenario.Db.Context.SalesInvoices.SingleAsync()).NfeStatus = status;
        await scenario.Db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            SalesCorrection(scenario.Db, sefaz).ExecuteAsync(scenario.InvoiceKey, Text, "tester"));

        Assert.Equal("Só NF-e autorizada recebe carta de correção.", ex.Message);
        Assert.Empty(sefaz.CorrectionRequests);
    }

    [Fact]
    public async Task Cancelled_document_is_refused()
    {
        var (scenario, sefaz) = await AuthorizedAsync();
        (await scenario.Db.Context.SalesInvoices.SingleAsync()).InvoiceStatus = InvoiceStatus.Cancelled;
        await scenario.Db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            SalesCorrection(scenario.Db, sefaz).ExecuteAsync(scenario.InvoiceKey, Text, "tester"));

        Assert.Equal("Documento cancelado não recebe carta de correção.", ex.Message);
        Assert.Empty(sefaz.CorrectionRequests);
    }

    [Fact]
    public async Task Missing_document_is_not_found()
    {
        var (scenario, sefaz) = await AuthorizedAsync();

        await Assert.ThrowsAsync<NotFoundException>(() =>
            SalesCorrection(scenario.Db, sefaz).ExecuteAsync(Guid.NewGuid(), Text, "tester"));
    }

    [Fact]
    public async Task Twenty_first_correction_is_refused_without_calling_sefaz()
    {
        var (scenario, sefaz) = await AuthorizedAsync();
        var store = new SiagroB1.Application.Services.Nfe.SalesInvoiceNfeStore(scenario.Db);
        var invoice = (await store.FindAsync(scenario.InvoiceKey))!;
        for (var sequence = 1; sequence <= 20; sequence++)
            store.AddCorrection(invoice, sequence, Text, FakeNfeSefazClient.CorrectionRegistered(sequence, Text), "tester");
        await scenario.Db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            SalesCorrection(scenario.Db, sefaz).ExecuteAsync(scenario.InvoiceKey, Text, "tester"));

        Assert.Equal("Limite de 20 cartas de correção atingido para esta NF-e.", ex.Message);
        Assert.Empty(sefaz.CorrectionRequests);
    }

    [Fact]
    public async Task Refusal_saves_nothing_and_shows_the_sefaz_message()
    {
        var (scenario, sefaz) = await AuthorizedAsync();
        sefaz.CorrectionResponses.Enqueue(_ => new NfeEventResult(501, "Rejeição: Prazo do evento expirado"));

        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            SalesCorrection(scenario.Db, sefaz).ExecuteAsync(scenario.InvoiceKey, Text, "tester"));

        Assert.Equal("Carta de correção recusada pela SEFAZ: 501 - Rejeição: Prazo do evento expirado", ex.Message);
        Assert.False(await scenario.Db.Context.SalesInvoiceNfeCorrections.AnyAsync());
    }

    [Fact]
    public async Task No_answer_saves_nothing_and_asks_to_consult()
    {
        var (scenario, sefaz) = await AuthorizedAsync();
        sefaz.CorrectionResponses.Enqueue(_ => throw new NfeCommunicationException("Tempo esgotado."));

        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            SalesCorrection(scenario.Db, sefaz).ExecuteAsync(scenario.InvoiceKey, Text, "tester"));

        Assert.Equal("Sem resposta da SEFAZ na carta de correção: use Consultar situação antes de tentar de novo.", ex.Message);
        Assert.False(await scenario.Db.Context.SalesInvoiceNfeCorrections.AnyAsync());
    }

    [Fact]
    public async Task Unexpected_failure_reports_the_technical_detail()
    {
        var (scenario, sefaz) = await AuthorizedAsync();
        sefaz.CorrectionResponses.Enqueue(_ => throw new FileNotFoundException("envCCe_v1.00.xsd"));

        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            SalesCorrection(scenario.Db, sefaz).ExecuteAsync(scenario.InvoiceKey, Text, "tester"));

        Assert.Contains("detalhe técnico: envCCe_v1.00.xsd", ex.Message);
    }

    [Fact]
    public async Task Duplicate_event_imports_the_lost_correction_from_the_consult()
    {
        // A nº 1 entrou na SEFAZ, mas a resposta se perdeu: o próximo envio repete a sequência 1.
        var (scenario, sefaz) = await AuthorizedAsync();
        sefaz.CorrectionResponses.Enqueue(_ => new NfeEventResult(573, "Rejeição: Duplicidade de evento"));
        sefaz.ConsultResponses.Enqueue(key => FakeNfeSefazClient.Authorized(key) with
        {
            Corrections = [FakeNfeSefazClient.CorrectionRegistered(1, "Texto que entrou antes")],
        });

        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            SalesCorrection(scenario.Db, sefaz).ExecuteAsync(scenario.InvoiceKey, Text, "tester"));

        Assert.Equal(
            "A carta de correção nº 1 já estava registrada na SEFAZ com outro envio e foi importada. " +
            "O texto enviado agora não foi registrado: envie de novo.", ex.Message);
        var row = await scenario.Db.Context.SalesInvoiceNfeCorrections.AsNoTracking().SingleAsync();
        Assert.Equal(1, row.Sequence);
        Assert.Equal("Texto que entrou antes", row.Text);
    }

    [Fact]
    public async Task Duplicate_event_with_the_same_text_is_a_success()
    {
        var (scenario, sefaz) = await AuthorizedAsync();
        sefaz.CorrectionResponses.Enqueue(_ => new NfeEventResult(573, "Rejeição: Duplicidade de evento"));
        sefaz.ConsultResponses.Enqueue(key => FakeNfeSefazClient.Authorized(key) with
        {
            Corrections = [FakeNfeSefazClient.CorrectionRegistered(1, Text)],
        });

        var outcome = await SalesCorrection(scenario.Db, sefaz).ExecuteAsync(scenario.InvoiceKey, Text, "tester");

        Assert.Equal(1, outcome.Sequence);
        Assert.Single(await scenario.Db.Context.SalesInvoiceNfeCorrections.AsNoTracking().ToListAsync());
    }

    [Fact]
    public async Task Duplicate_event_not_confirmed_by_the_consult_is_refused()
    {
        var (scenario, sefaz) = await AuthorizedAsync();
        sefaz.CorrectionResponses.Enqueue(_ => new NfeEventResult(573, "Rejeição: Duplicidade de evento"));
        sefaz.ConsultResponses.Enqueue(key => FakeNfeSefazClient.Authorized(key));

        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            SalesCorrection(scenario.Db, sefaz).ExecuteAsync(scenario.InvoiceKey, Text, "tester"));

        Assert.StartsWith("A SEFAZ informou evento duplicado, mas a consulta não trouxe a carta nº 1:", ex.Message);
        Assert.False(await scenario.Db.Context.SalesInvoiceNfeCorrections.AnyAsync());
    }
}
