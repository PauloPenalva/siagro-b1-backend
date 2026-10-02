using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Nfe;
using SiagroB1.Application.Services.Taxes;
using SiagroB1.Application.Services.SalesInvoices;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Application.Tests.Nfe;

/// <summary>"Emitir NF-e" (spec §9.2), com a SEFAZ simulada e o XML assinado de verdade.</summary>
public class SalesInvoicesNfeIssueServiceTests
{
    private static SalesInvoicesNfeIssueService Issue(
        NfeScenario scenario, FakeNfeSefazClient sefaz, SalesInvoicesConfirmService confirm,
        string erp = "STANDALONE", FakeNfeNumberReservationService? reservation = null)
    {
        var config = NfeTestSeed.Config(erp);
        var options = new NfeOptions(config);

        return new SalesInvoicesNfeIssueService(
            scenario.Db,
            new TaxCalculationGate(scenario.Db, config),
            new NfeReadinessValidator(scenario.Db, options),
            new BranchNfeSettingsService(scenario.Db, options, sefaz),
            reservation ?? new FakeNfeNumberReservationService(),
            sefaz,
            new SalesInvoiceNfeResultHandler(scenario.Db, confirm),
            options);
    }

    private static Task<Domain.Entities.SalesInvoice> ReloadAsync(NfeScenario scenario) =>
        TestDb.CreateUnitOfWork(scenario.DatabaseName).Context.SalesInvoices.AsNoTracking()
            .SingleAsync(i => i.Key == scenario.InvoiceKey);

    [Fact]
    public async Task Authorized_nfe_is_saved_and_the_document_confirmed()
    {
        var scenario = await NfeTestSeed.SeedAsync();
        var sefaz = new FakeNfeSefazClient();
        sefaz.AuthorizeResponses.Enqueue(key => FakeNfeSefazClient.Authorized(key));
        var confirm = new RecordingConfirmService(scenario.Db);

        var outcome = await Issue(scenario, sefaz, confirm).ExecuteAsync(scenario.InvoiceKey, "tester");

        var invoice = await ReloadAsync(scenario);
        Assert.Equal(NfeStatus.Authorized, outcome.NfeStatus);
        Assert.Equal(NfeStatus.Authorized, invoice.NfeStatus);
        Assert.Equal(InvoiceStatus.Confirmed, invoice.InvoiceStatus);
        Assert.Equal("000000001", invoice.TaxDocumentNumber);
        Assert.Equal("1", invoice.TaxDocumentSeries);
        Assert.Equal(44, invoice.ChaveNFe!.Length);
        Assert.Equal("135260000000001", invoice.NfeProtocol);
        Assert.Equal(NfeEnvironment.Homologation, invoice.NfeEnvironment);
        Assert.Equal(1, confirm.Calls);

        var xmls = await scenario.Db.Context.SalesInvoiceNfeXmls.AsNoTracking().ToListAsync();
        Assert.Contains(xmls, x => x.Kind == SalesInvoiceNfeXmlKind.Signed && x.Xml.Contains("NF-E EMITIDA EM AMBIENTE DE HOMOLOGACAO - SEM VALOR FISCAL"));
        Assert.Contains(xmls, x => x.Kind == SalesInvoiceNfeXmlKind.Authorized && x.Xml.Contains("<nfeProc") && x.Xml.Contains("<protNFe"));
    }

    [Fact]
    public async Task Processing_and_signed_xml_are_saved_before_sending()
    {
        var scenario = await NfeTestSeed.SeedAsync();
        var sefaz = new FakeNfeSefazClient();
        NfeStatus? statusAtSend = null;
        var signedAtSend = 0;
        sefaz.BeforeAuthorize = async () =>
        {
            var other = TestDb.CreateUnitOfWork(scenario.DatabaseName).Context;
            statusAtSend = (await other.SalesInvoices.AsNoTracking().SingleAsync()).NfeStatus;
            signedAtSend = await other.SalesInvoiceNfeXmls.CountAsync(x => x.Kind == SalesInvoiceNfeXmlKind.Signed);
        };
        sefaz.AuthorizeResponses.Enqueue(key => FakeNfeSefazClient.Authorized(key));

        await Issue(scenario, sefaz, new RecordingConfirmService(scenario.Db)).ExecuteAsync(scenario.InvoiceKey, "tester");

        Assert.Equal(NfeStatus.Processing, statusAtSend);
        Assert.Equal(1, signedAtSend);
    }

    [Fact]
    public async Task Rejection_keeps_the_document_pending_with_the_number()
    {
        var scenario = await NfeTestSeed.SeedAsync();
        var sefaz = new FakeNfeSefazClient();
        sefaz.AuthorizeResponses.Enqueue(_ => FakeNfeSefazClient.Rejected());
        var confirm = new RecordingConfirmService(scenario.Db);

        var outcome = await Issue(scenario, sefaz, confirm).ExecuteAsync(scenario.InvoiceKey, "tester");

        var invoice = await ReloadAsync(scenario);
        Assert.Equal(NfeStatus.Rejected, outcome.NfeStatus);
        Assert.Equal(InvoiceStatus.Pending, invoice.InvoiceStatus);
        Assert.Equal("209", invoice.NfeStatusCode);
        Assert.Contains("IE do emitente", invoice.NfeStatusReason);
        Assert.Equal("000000001", invoice.TaxDocumentNumber);
        Assert.Equal(0, confirm.Calls);
    }

    [Fact]
    public async Task Retry_after_rejection_reuses_the_number_and_the_random_code()
    {
        var scenario = await NfeTestSeed.SeedAsync();
        var sefaz = new FakeNfeSefazClient();
        sefaz.AuthorizeResponses.Enqueue(_ => FakeNfeSefazClient.Rejected());
        sefaz.AuthorizeResponses.Enqueue(key => FakeNfeSefazClient.Authorized(key));
        var reservation = new FakeNfeNumberReservationService();
        var service = Issue(scenario, sefaz, new RecordingConfirmService(scenario.Db), reservation: reservation);

        await service.ExecuteAsync(scenario.InvoiceKey, "tester");
        var randomCode = (await ReloadAsync(scenario)).NfeRandomCode;
        await service.ExecuteAsync(scenario.InvoiceKey, "tester");

        var invoice = await ReloadAsync(scenario);
        Assert.Equal(NfeStatus.Authorized, invoice.NfeStatus);
        Assert.Equal("000000001", invoice.TaxDocumentNumber);
        Assert.Equal(randomCode, invoice.NfeRandomCode);
        Assert.Equal(1, reservation.Calls);
        Assert.Equal(sefaz.Sent[0].AccessKey[..34], sefaz.Sent[1].AccessKey[..34]); // mesma UF/AAMM/CNPJ/modelo/série/número
    }

    [Fact]
    public async Task Denied_nfe_blocks_new_attempts()
    {
        var scenario = await NfeTestSeed.SeedAsync();
        var sefaz = new FakeNfeSefazClient();
        sefaz.AuthorizeResponses.Enqueue(_ => new Fiscal.Nfe.NfeSefazResult(302, "Uso Denegado: Irregularidade fiscal do destinatário", "135260000000002"));
        var service = Issue(scenario, sefaz, new RecordingConfirmService(scenario.Db));

        var outcome = await service.ExecuteAsync(scenario.InvoiceKey, "tester");
        var ex = await Assert.ThrowsAsync<DefaultException>(() => service.ExecuteAsync(scenario.InvoiceKey, "tester"));

        Assert.Equal(NfeStatus.Denied, outcome.NfeStatus);
        Assert.Contains("denegada", ex.Message);
    }

    [Fact]
    public async Task Duplicate_is_consulted_and_followed()
    {
        var scenario = await NfeTestSeed.SeedAsync();
        var sefaz = new FakeNfeSefazClient();
        sefaz.AuthorizeResponses.Enqueue(_ => new Fiscal.Nfe.NfeSefazResult(204, "Rejeição: Duplicidade de NF-e"));
        sefaz.ConsultResponses.Enqueue(key => FakeNfeSefazClient.Authorized(key));
        var confirm = new RecordingConfirmService(scenario.Db);

        var outcome = await Issue(scenario, sefaz, confirm).ExecuteAsync(scenario.InvoiceKey, "tester");

        Assert.Equal(NfeStatus.Authorized, outcome.NfeStatus);
        Assert.Single(sefaz.Consulted);
        Assert.Equal(1, confirm.Calls);
    }

    [Fact]
    public async Task No_response_keeps_the_document_processing()
    {
        var scenario = await NfeTestSeed.SeedAsync();
        var sefaz = new FakeNfeSefazClient();
        sefaz.AuthorizeResponses.Enqueue(FakeNfeSefazClient.NoResponse);

        var outcome = await Issue(scenario, sefaz, new RecordingConfirmService(scenario.Db)).ExecuteAsync(scenario.InvoiceKey, "tester");

        var invoice = await ReloadAsync(scenario);
        Assert.Equal(NfeStatus.Processing, outcome.NfeStatus);
        Assert.Equal(NfeStatus.Processing, invoice.NfeStatus);
        Assert.Contains("Consultar situação", invoice.NfeStatusReason);
    }

    [Fact]
    public async Task Unreadable_sefaz_answer_keeps_the_document_processing()
    {
        var scenario = await NfeTestSeed.SeedAsync();
        var sefaz = new FakeNfeSefazClient();
        sefaz.AuthorizeResponses.Enqueue(_ => throw new InvalidOperationException("XML inválido"));

        var outcome = await Issue(scenario, sefaz, new RecordingConfirmService(scenario.Db)).ExecuteAsync(scenario.InvoiceKey, "tester");

        var invoice = await ReloadAsync(scenario);
        Assert.Equal(NfeStatus.Processing, outcome.NfeStatus);
        Assert.Equal(NfeStatus.Processing, invoice.NfeStatus);
        Assert.Contains("detalhe técnico", invoice.NfeStatusReason);
    }

    [Fact]
    public async Task Local_schema_failure_is_rejected_without_sending()
    {
        var scenario = await NfeTestSeed.SeedAsync();
        (await scenario.Db.Context.SalesInvoicesItems.SingleAsync()).Ncm = "1201";
        await scenario.Db.SaveChangesAsync();
        var sefaz = new FakeNfeSefazClient();

        var outcome = await Issue(scenario, sefaz, new RecordingConfirmService(scenario.Db)).ExecuteAsync(scenario.InvoiceKey, "tester");

        Assert.Equal(NfeStatus.Rejected, outcome.NfeStatus);
        Assert.Contains("validação local", outcome.Reason);
        Assert.Empty(sefaz.Sent);
        Assert.Empty(await scenario.Db.Context.SalesInvoiceNfeXmls.ToListAsync());
    }

    [Fact]
    public async Task Confirmation_failure_keeps_the_nfe_and_records_the_error_without_partial_confirmation()
    {
        var scenario = await NfeTestSeed.SeedAsync();
        var sefaz = new FakeNfeSefazClient();
        sefaz.AuthorizeResponses.Enqueue(key => FakeNfeSefazClient.Authorized(key));
        var confirm = new RecordingConfirmService(scenario.Db, failWith: new DefaultException("Liberação de entrega sem saldo."));

        var outcome = await Issue(scenario, sefaz, confirm).ExecuteAsync(scenario.InvoiceKey, "tester");

        var invoice = await ReloadAsync(scenario);
        Assert.Equal(NfeStatus.Authorized, invoice.NfeStatus);
        Assert.Equal(InvoiceStatus.Pending, invoice.InvoiceStatus);      // a mudança no rastreador foi descartada
        Assert.Contains("sem saldo", invoice.NfeConfirmationError);
        Assert.Contains("sem saldo", outcome.ConfirmationError);
    }

    [Fact]
    public async Task Rule_inactive_is_refused()
    {
        var scenario = await NfeTestSeed.SeedAsync();

        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            Issue(scenario, new FakeNfeSefazClient(), new RecordingConfirmService(scenario.Db), erp: "SAPB1")
                .ExecuteAsync(scenario.InvoiceKey, "tester"));

        Assert.Contains("não emite NF-e", ex.Message);
    }

    [Fact]
    public async Task Line_without_calculated_taxes_is_refused()
    {
        var scenario = await NfeTestSeed.SeedAsync();
        (await scenario.Db.Context.SalesInvoicesItems.SingleAsync()).CstIcms = null;
        await scenario.Db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            Issue(scenario, new FakeNfeSefazClient(), new RecordingConfirmService(scenario.Db)).ExecuteAsync(scenario.InvoiceKey, "tester"));

        Assert.Contains("Salve o documento", ex.Message);
    }

    [Fact]
    public async Task Return_document_is_refused()
    {
        var scenario = await NfeTestSeed.SeedAsync();
        (await scenario.Db.Context.SalesInvoices.SingleAsync()).InvoiceType = SalesInvoiceType.Return;
        await scenario.Db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            Issue(scenario, new FakeNfeSefazClient(), new RecordingConfirmService(scenario.Db)).ExecuteAsync(scenario.InvoiceKey, "tester"));

        Assert.Contains("Normal", ex.Message);
    }

    [Fact]
    public async Task Batch_received_keeps_the_document_processing()
    {
        var scenario = await NfeTestSeed.SeedAsync();
        var sefaz = new FakeNfeSefazClient();
        sefaz.AuthorizeResponses.Enqueue(_ => new Fiscal.Nfe.NfeSefazResult(103, "Lote recebido com sucesso"));

        var outcome = await Issue(scenario, sefaz, new RecordingConfirmService(scenario.Db)).ExecuteAsync(scenario.InvoiceKey, "tester");

        var invoice = await ReloadAsync(scenario);
        Assert.Equal(NfeStatus.Processing, outcome.NfeStatus);
        Assert.Equal(NfeStatus.Processing, invoice.NfeStatus);
        Assert.Contains("Consultar situação", invoice.NfeStatusReason);
    }

    [Fact]
    public async Task Duplicate_with_a_protocol_that_matches_no_saved_xml_stays_processing()
    {
        var scenario = await NfeTestSeed.SeedAsync();
        var sefaz = new FakeNfeSefazClient();
        sefaz.AuthorizeResponses.Enqueue(_ => new Fiscal.Nfe.NfeSefazResult(204, "Rejeição: Duplicidade de NF-e"));
        sefaz.ConsultResponses.Enqueue(key => FakeNfeSefazClient.Authorized(key, digVal: "naoconfere="));
        var confirm = new RecordingConfirmService(scenario.Db);

        var outcome = await Issue(scenario, sefaz, confirm).ExecuteAsync(scenario.InvoiceKey, "tester");

        Assert.Equal(NfeStatus.Processing, outcome.NfeStatus);
        Assert.Equal(0, confirm.Calls);
        Assert.DoesNotContain(
            await scenario.Db.Context.SalesInvoiceNfeXmls.AsNoTracking().ToListAsync(),
            x => x.Kind == SalesInvoiceNfeXmlKind.Authorized);
    }

    [Fact]
    public async Task Manually_typed_number_is_replaced_by_the_reserved_one()
    {
        var scenario = await NfeTestSeed.SeedAsync();
        var seeded = await scenario.Db.Context.SalesInvoices.SingleAsync();
        seeded.TaxDocumentNumber = "ABC";
        seeded.TaxDocumentSeries = "9";
        await scenario.Db.SaveChangesAsync();
        var sefaz = new FakeNfeSefazClient();
        sefaz.AuthorizeResponses.Enqueue(key => FakeNfeSefazClient.Authorized(key));
        var reservation = new FakeNfeNumberReservationService();

        await Issue(scenario, sefaz, new RecordingConfirmService(scenario.Db), reservation: reservation)
            .ExecuteAsync(scenario.InvoiceKey, "tester");

        var invoice = await ReloadAsync(scenario);
        Assert.Equal("000000001", invoice.TaxDocumentNumber);
        Assert.Equal("1", invoice.TaxDocumentSeries);
        Assert.Equal(1, reservation.Calls);
    }
}
