using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Nfe;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Fiscal.Nfe;
using static SiagroB1.Application.Tests.Support.NfeVoidNumberTestServices;

namespace SiagroB1.Application.Tests.Nfe;

/// <summary>Inutilização da numeração da NF-e rejeitada do documento de saída (spec 2026-10-08).</summary>
public class SalesInvoicesNfeVoidNumberServiceTests
{
    private const string Reason = "NF-e rejeitada e documento cancelado";

    private static async Task<(NfeScenario Scenario, FakeNfeSefazClient Sefaz)> RejectedAsync(bool sentToSefaz = true)
    {
        var scenario = await NfeTestSeed.SeedAsync();
        await RejectAndCancelSaleAsync(scenario.Db, scenario.InvoiceKey, sentToSefaz);
        return (scenario, new FakeNfeSefazClient());
    }

    [Fact]
    public async Task Homologated_void_marks_the_nfe_voided_and_keeps_the_proc_xml()
    {
        var (scenario, sefaz) = await RejectedAsync();
        sefaz.VoidNumberResponses.Enqueue(FakeNfeSefazClient.NumberVoided);

        var outcome = await SalesVoid(scenario.Db, sefaz).ExecuteAsync(scenario.InvoiceKey, Reason, "tester");

        Assert.Equal(NfeStatus.Voided, outcome.NfeStatus);
        Assert.Equal("102", outcome.StatusCode);
        var saved = await scenario.Db.Context.SalesInvoices.AsNoTracking().SingleAsync();
        Assert.Equal(NfeStatus.Voided, saved.NfeStatus);
        Assert.Equal(FakeNfeSefazClient.VoidNumberProtocol, saved.NfeProtocol);
        Assert.Equal(InvoiceStatus.Cancelled, saved.InvoiceStatus);
        Assert.Equal(RejectedAccessKey, saved.ChaveNFe);
        var xml = await scenario.Db.Context.SalesInvoiceNfeXmls.AsNoTracking().SingleAsync(x => x.Kind == NfeXmlKind.NumberVoid);
        Assert.Contains("<procInutNFe", xml.Xml);
    }

    [Fact]
    public async Task Request_takes_year_from_the_key_and_number_from_the_document()
    {
        var (scenario, sefaz) = await RejectedAsync();
        sefaz.VoidNumberResponses.Enqueue(FakeNfeSefazClient.NumberVoided);

        await SalesVoid(scenario.Db, sefaz).ExecuteAsync(scenario.InvoiceKey, "  " + Reason + "  ", "tester");

        var request = Assert.Single(sefaz.VoidNumberRequests);
        Assert.Equal(new NfeVoidNumberRequest(26, "12345678000195", 1, 233, Reason), request);
        Assert.Equal(NfeEnvironment.Homologation, Assert.Single(sefaz.VoidNumberSettings).Environment);
    }

    [Fact]
    public async Task Locally_rejected_nfe_uses_the_current_brasilia_year_and_the_branch()
    {
        var (scenario, sefaz) = await RejectedAsync(sentToSefaz: false);
        sefaz.VoidNumberResponses.Enqueue(FakeNfeSefazClient.NumberVoided);

        await SalesVoid(scenario.Db, sefaz).ExecuteAsync(scenario.InvoiceKey, Reason, "tester");

        var request = Assert.Single(sefaz.VoidNumberRequests);
        var year = TimeZoneInfo.ConvertTime(DateTimeOffset.Now, NfeIssueInputAssembler.BrasiliaZone).Year % 100;
        Assert.Equal(year, request.Year);
        Assert.Equal("12345678000195", request.TaxId);
        Assert.Equal(NfeEnvironment.Homologation, Assert.Single(sefaz.VoidNumberSettings).Environment);
    }

    [Fact]
    public async Task Alphanumeric_branch_tax_id_keeps_its_letters_like_the_emission()
    {
        var (scenario, sefaz) = await RejectedAsync();
        var branch = await scenario.Db.Context.Branchs.SingleAsync();
        branch.TaxId = "12.ABC.345/01DE-35";
        await scenario.Db.Context.SaveChangesAsync();
        sefaz.VoidNumberResponses.Enqueue(FakeNfeSefazClient.NumberVoided);

        await SalesVoid(scenario.Db, sefaz).ExecuteAsync(scenario.InvoiceKey, Reason, "tester");

        Assert.Equal("12ABC34501DE35", Assert.Single(sefaz.VoidNumberRequests).TaxId);
    }

    [Theory]
    [InlineData(256)]
    [InlineData(563)]
    public async Task Already_voided_at_sefaz_marks_voided_without_proc_xml(int code)
    {
        var (scenario, sefaz) = await RejectedAsync();
        sefaz.VoidNumberResponses.Enqueue(_ => new NfeVoidNumberResult(code, "Rejeição: já inutilizada"));

        var outcome = await SalesVoid(scenario.Db, sefaz).ExecuteAsync(scenario.InvoiceKey, Reason, "tester");

        Assert.Equal(NfeStatus.Voided, outcome.NfeStatus);
        Assert.Equal(code.ToString(), outcome.StatusCode);
        Assert.False(await scenario.Db.Context.SalesInvoiceNfeXmls.AnyAsync(x => x.Kind == NfeXmlKind.NumberVoid));
    }

    [Fact]
    public async Task Sefaz_refusal_changes_nothing()
    {
        var (scenario, sefaz) = await RejectedAsync();
        sefaz.VoidNumberResponses.Enqueue(_ => new NfeVoidNumberResult(241, "Rejeição: Um número da faixa já foi utilizado"));

        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            SalesVoid(scenario.Db, sefaz).ExecuteAsync(scenario.InvoiceKey, Reason, "tester"));

        Assert.Equal("Inutilização recusada pela SEFAZ: 241 - Rejeição: Um número da faixa já foi utilizado", ex.Message);
        Assert.Equal(NfeStatus.Rejected, (await scenario.Db.Context.SalesInvoices.AsNoTracking().SingleAsync()).NfeStatus);
    }

    [Fact]
    public async Task No_answer_changes_nothing()
    {
        var (scenario, sefaz) = await RejectedAsync();
        sefaz.VoidNumberResponses.Enqueue(_ => throw new NfeCommunicationException("Tempo esgotado."));

        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            SalesVoid(scenario.Db, sefaz).ExecuteAsync(scenario.InvoiceKey, Reason, "tester"));

        Assert.Equal(NfeVoidNumberServiceBase<Domain.Entities.SalesInvoice>.NoAnswerMessage, ex.Message);
        Assert.Equal(NfeStatus.Rejected, (await scenario.Db.Context.SalesInvoices.AsNoTracking().SingleAsync()).NfeStatus);
    }

    [Fact]
    public async Task Unexpected_failure_changes_nothing_and_shows_the_detail()
    {
        var (scenario, sefaz) = await RejectedAsync();
        sefaz.VoidNumberResponses.Enqueue(_ => throw new FileNotFoundException("inutNFe_v4.00.xsd"));

        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            SalesVoid(scenario.Db, sefaz).ExecuteAsync(scenario.InvoiceKey, Reason, "tester"));

        Assert.StartsWith(NfeVoidNumberServiceBase<Domain.Entities.SalesInvoice>.NoAnswerMessage, ex.Message);
        Assert.Contains("inutNFe_v4.00.xsd", ex.Message);
        Assert.Equal(NfeStatus.Rejected, (await scenario.Db.Context.SalesInvoices.AsNoTracking().SingleAsync()).NfeStatus);
    }

    [Theory]
    [InlineData("curta demais")]
    [InlineData("")]
    [InlineData(null)]
    public async Task Justification_must_have_15_to_255_characters(string? justification)
    {
        var (scenario, sefaz) = await RejectedAsync();

        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            SalesVoid(scenario.Db, sefaz).ExecuteAsync(scenario.InvoiceKey, justification, "tester"));

        Assert.Equal("A justificativa deve ter entre 15 e 255 caracteres.", ex.Message);
        Assert.Empty(sefaz.VoidNumberRequests);
    }

    [Fact]
    public async Task Document_must_be_cancelled()
    {
        var (scenario, sefaz) = await RejectedAsync();
        (await scenario.Db.Context.SalesInvoices.SingleAsync()).InvoiceStatus = InvoiceStatus.Confirmed;
        await scenario.Db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            SalesVoid(scenario.Db, sefaz).ExecuteAsync(scenario.InvoiceKey, Reason, "tester"));

        Assert.Equal(NfeVoidNumberServiceBase<Domain.Entities.SalesInvoice>.NotCancelledMessage, ex.Message);
        Assert.Empty(sefaz.VoidNumberRequests);
    }

    [Theory]
    [InlineData(NfeStatus.None)]
    [InlineData(NfeStatus.Processing)]
    [InlineData(NfeStatus.Authorized)]
    [InlineData(NfeStatus.Denied)]
    [InlineData(NfeStatus.Cancelled)]
    public async Task Only_rejected_nfe_is_voided(NfeStatus status)
    {
        var (scenario, sefaz) = await RejectedAsync();
        (await scenario.Db.Context.SalesInvoices.SingleAsync()).NfeStatus = status;
        await scenario.Db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            SalesVoid(scenario.Db, sefaz).ExecuteAsync(scenario.InvoiceKey, Reason, "tester"));

        Assert.Equal(NfeVoidNumberServiceBase<Domain.Entities.SalesInvoice>.NotRejectedMessage, ex.Message);
        Assert.Empty(sefaz.VoidNumberRequests);
    }

    [Fact]
    public async Task Second_void_is_refused_before_sefaz()
    {
        var (scenario, sefaz) = await RejectedAsync();
        sefaz.VoidNumberResponses.Enqueue(FakeNfeSefazClient.NumberVoided);
        await SalesVoid(scenario.Db, sefaz).ExecuteAsync(scenario.InvoiceKey, Reason, "tester");

        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            SalesVoid(scenario.Db, sefaz).ExecuteAsync(scenario.InvoiceKey, Reason, "tester"));

        Assert.Equal(NfeVoidNumberServiceBase<Domain.Entities.SalesInvoice>.AlreadyVoidedMessage, ex.Message);
        Assert.Single(sefaz.VoidNumberRequests);
    }

    [Fact]
    public async Task Document_without_number_is_refused()
    {
        var (scenario, sefaz) = await RejectedAsync();
        var invoice = await scenario.Db.Context.SalesInvoices.SingleAsync();
        invoice.TaxDocumentNumber = null;
        invoice.TaxDocumentSeries = null;
        await scenario.Db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            SalesVoid(scenario.Db, sefaz).ExecuteAsync(scenario.InvoiceKey, Reason, "tester"));

        Assert.Equal(NfeVoidNumberServiceBase<Domain.Entities.SalesInvoice>.NoNumberMessage, ex.Message);
        Assert.Empty(sefaz.VoidNumberRequests);
    }

    [Fact]
    public async Task Missing_document_is_not_found()
    {
        var (scenario, sefaz) = await RejectedAsync();

        await Assert.ThrowsAsync<NotFoundException>(() =>
            SalesVoid(scenario.Db, sefaz).ExecuteAsync(Guid.NewGuid(), Reason, "tester"));
    }
}
