using System.Globalization;
using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
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
        string erp = "STANDALONE", FakeNfeNumberReservationService? reservation = null,
        Func<DateTimeOffset>? clock = null, TimeZoneInfo? storageZone = null)
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
            new SalesInvoiceNfeResultHandler(scenario.Db, confirm, NullLogger<SalesInvoiceNfeResultHandler>.Instance),
            options,
            NullLogger<SalesInvoicesNfeIssueService>.Instance,
            clock ?? NfeTestSeed.Clock,
            // As datas semeadas são de Brasília: o teste não depende do fuso da máquina.
            storageZone ?? NfeIssueInputAssembler.BrasiliaZone);
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
        Assert.Contains(xmls, x => x.Kind == NfeXmlKind.Signed && x.Xml.Contains("NF-E EMITIDA EM AMBIENTE DE HOMOLOGACAO - SEM VALOR FISCAL"));
        Assert.Contains(xmls, x => x.Kind == NfeXmlKind.Authorized && x.Xml.Contains("<nfeProc") && x.Xml.Contains("<protNFe"));
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
            signedAtSend = await other.SalesInvoiceNfeXmls.CountAsync(x => x.Kind == NfeXmlKind.Signed);
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

    /// <summary>Reenvio de uma rejeitada: o código antigo não pode ficar ao lado do "sem resposta".</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task No_response_clears_the_stale_status_code(bool unexpectedFailure)
    {
        var scenario = await NfeTestSeed.SeedAsync();
        var previous = await scenario.Db.Context.SalesInvoices.SingleAsync();
        previous.NfeStatus = NfeStatus.Rejected;
        previous.NfeStatusCode = "209";
        await scenario.Db.SaveChangesAsync();
        var sefaz = new FakeNfeSefazClient();
        sefaz.AuthorizeResponses.Enqueue(unexpectedFailure
            ? _ => throw new InvalidOperationException("XML inválido")
            : FakeNfeSefazClient.NoResponse);

        await Issue(scenario, sefaz, new RecordingConfirmService(scenario.Db)).ExecuteAsync(scenario.InvoiceKey, "tester");

        var invoice = await ReloadAsync(scenario);
        Assert.Equal(NfeStatus.Processing, invoice.NfeStatus);
        Assert.Null(invoice.NfeStatusCode);
        Assert.StartsWith("Sem resposta da SEFAZ", invoice.NfeStatusReason);
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
            x => x.Kind == NfeXmlKind.Authorized);
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

    [Fact]
    public async Task Emission_in_progress_is_refused_before_anything_is_reserved_or_sent()
    {
        var scenario = await NfeTestSeed.SeedAsync();
        var sefaz = new FakeNfeSefazClient();
        var reservation = new FakeNfeNumberReservationService { EmissionBusy = true };

        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            Issue(scenario, sefaz, new RecordingConfirmService(scenario.Db), reservation: reservation)
                .ExecuteAsync(scenario.InvoiceKey, "tester"));

        Assert.Equal("A emissão deste documento já está em andamento. Aguarde e consulte a situação.", ex.Message);
        Assert.Empty(sefaz.Sent);
        Assert.Equal(0, reservation.Calls);
    }

    [Fact]
    public async Task The_emission_lock_is_released_when_the_emission_ends()
    {
        var scenario = await NfeTestSeed.SeedAsync();
        var sefaz = new FakeNfeSefazClient();
        sefaz.AuthorizeResponses.Enqueue(key => FakeNfeSefazClient.Authorized(key));
        var reservation = new FakeNfeNumberReservationService();

        await Issue(scenario, sefaz, new RecordingConfirmService(scenario.Db), reservation: reservation)
            .ExecuteAsync(scenario.InvoiceKey, "tester");

        Assert.Equal(1, reservation.LockAttempts);
        Assert.Equal(0, reservation.LocksHeld);
    }

    /// <summary>539: mesmo número com outra chave. Consultar não resolve — a reserva é que está errada.</summary>
    [Fact]
    public async Task Number_already_used_with_another_key_is_rejected_without_consulting_and_releases_the_reservation()
    {
        var scenario = await NfeTestSeed.SeedAsync();
        var sefaz = new FakeNfeSefazClient();
        sefaz.AuthorizeResponses.Enqueue(_ => new Fiscal.Nfe.NfeSefazResult(539, "Rejeição: Duplicidade de NF-e, com diferença na Chave de Acesso"));
        sefaz.AuthorizeResponses.Enqueue(key => FakeNfeSefazClient.Authorized(key));
        var reservation = new FakeNfeNumberReservationService();
        var service = Issue(scenario, sefaz, new RecordingConfirmService(scenario.Db), reservation: reservation);

        var outcome = await service.ExecuteAsync(scenario.InvoiceKey, "tester");

        Assert.Equal(NfeStatus.Rejected, outcome.NfeStatus);
        Assert.Empty(sefaz.Consulted);
        var invoice = await ReloadAsync(scenario);
        Assert.Equal("539", invoice.NfeStatusCode);
        Assert.StartsWith("Rejeição: Duplicidade de NF-e", invoice.NfeStatusReason);
        Assert.EndsWith("ajuste o Próximo número na Configuração da NF-e se o número já foi usado por outro sistema.", invoice.NfeStatusReason);
        Assert.Null(invoice.NfeRandomCode);
        Assert.Null(invoice.TaxDocumentNumber);
        Assert.Null(invoice.TaxDocumentSeries);
        Assert.Null(invoice.ChaveNFe);

        await service.ExecuteAsync(scenario.InvoiceKey, "tester");

        Assert.Equal(2, reservation.Calls);
        Assert.Equal("000000002", (await ReloadAsync(scenario)).TaxDocumentNumber);
    }

    [Fact]
    public async Task Retry_after_the_series_changed_reserves_a_new_number()
    {
        var scenario = await NfeTestSeed.SeedAsync();
        var sefaz = new FakeNfeSefazClient();
        sefaz.AuthorizeResponses.Enqueue(_ => FakeNfeSefazClient.Rejected());
        sefaz.AuthorizeResponses.Enqueue(key => FakeNfeSefazClient.Authorized(key));
        var reservation = new FakeNfeNumberReservationService();
        var service = Issue(scenario, sefaz, new RecordingConfirmService(scenario.Db), reservation: reservation);
        await service.ExecuteAsync(scenario.InvoiceKey, "tester");
        (await scenario.Db.Context.BranchNfeSettings.SingleAsync()).Series = 2;
        await scenario.Db.SaveChangesAsync();

        await service.ExecuteAsync(scenario.InvoiceKey, "tester");

        var invoice = await ReloadAsync(scenario);
        Assert.Equal(2, reservation.Calls);
        Assert.Equal("2", invoice.TaxDocumentSeries);
        Assert.Equal("000000002", invoice.TaxDocumentNumber);
    }

    [Fact]
    public async Task Retry_after_the_environment_changed_reserves_a_new_number()
    {
        var scenario = await NfeTestSeed.SeedAsync();
        var sefaz = new FakeNfeSefazClient();
        sefaz.AuthorizeResponses.Enqueue(_ => FakeNfeSefazClient.Rejected());
        sefaz.AuthorizeResponses.Enqueue(_ => FakeNfeSefazClient.Rejected());
        var reservation = new FakeNfeNumberReservationService();
        var service = Issue(scenario, sefaz, new RecordingConfirmService(scenario.Db), reservation: reservation);
        await service.ExecuteAsync(scenario.InvoiceKey, "tester");
        (await scenario.Db.Context.BranchNfeSettings.SingleAsync()).Environment = NfeEnvironment.Production;
        await scenario.Db.SaveChangesAsync();

        await service.ExecuteAsync(scenario.InvoiceKey, "tester");

        Assert.Equal(2, reservation.Calls);
        Assert.Equal("000000002", (await ReloadAsync(scenario)).TaxDocumentNumber);
        var lastSigned = await scenario.Db.Context.SalesInvoiceNfeXmls.AsNoTracking()
            .OrderByDescending(x => x.CreatedAt).Select(x => x.Xml).FirstAsync();
        Assert.Contains("<tpAmb>1</tpAmb>", lastSigned);
    }

    [Fact]
    public async Task Document_dated_another_day_is_refused_before_reserving_a_number()
    {
        var scenario = await NfeTestSeed.SeedAsync();
        var sefaz = new FakeNfeSefazClient();
        var reservation = new FakeNfeNumberReservationService();
        var tomorrow = NfeTestSeed.Now.AddDays(1);

        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            Issue(scenario, sefaz, new RecordingConfirmService(scenario.Db), reservation: reservation, clock: () => tomorrow)
                .ExecuteAsync(scenario.InvoiceKey, "tester"));

        Assert.Equal(
            "A data do documento (02/10/2026) precisa ser a de hoje para emitir a NF-e: altere a data e salve (os impostos são recalculados).",
            ex.Message);
        Assert.Equal(0, reservation.Calls);
        Assert.Empty(sefaz.Sent);
    }

    [Fact]
    public async Task Document_created_in_the_evening_on_a_utc_server_is_emitted_the_same_day()
    {
        // Servidor em UTC: o documento criado às 22:30 de Brasília (02/10) fica gravado 03/10 01:30.
        var scenario = await NfeTestSeed.SeedAsync();
        (await scenario.Db.Context.SalesInvoices.SingleAsync()).InvoiceDate = new DateTime(2026, 10, 3, 1, 30, 0);
        await scenario.Db.SaveChangesAsync();
        var sefaz = new FakeNfeSefazClient();
        sefaz.AuthorizeResponses.Enqueue(key => FakeNfeSefazClient.Authorized(key));
        var evening = new DateTimeOffset(2026, 10, 2, 22, 45, 0, TimeSpan.FromHours(-3));

        var outcome = await Issue(scenario, sefaz, new RecordingConfirmService(scenario.Db),
                clock: () => evening, storageZone: TimeZoneInfo.Utc)
            .ExecuteAsync(scenario.InvoiceKey, "tester");

        Assert.Equal(NfeStatus.Authorized, outcome.NfeStatus);
    }

    [Fact]
    public async Task Document_from_another_day_on_a_utc_server_is_refused_with_the_brasilia_date()
    {
        // Gravado 03/10 02:00 num servidor em UTC = 02/10 23:00 em Brasília; a emissão é 03/10 10:00.
        // Pelo dia gravado (03/10) a nota sairia com data de um documento de 02/10.
        var scenario = await NfeTestSeed.SeedAsync();
        (await scenario.Db.Context.SalesInvoices.SingleAsync()).InvoiceDate = new DateTime(2026, 10, 3, 2, 0, 0);
        await scenario.Db.SaveChangesAsync();
        var reservation = new FakeNfeNumberReservationService();
        var nextMorning = new DateTimeOffset(2026, 10, 3, 10, 0, 0, TimeSpan.FromHours(-3));

        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            Issue(scenario, new FakeNfeSefazClient(), new RecordingConfirmService(scenario.Db), reservation: reservation,
                    clock: () => nextMorning, storageZone: TimeZoneInfo.Utc)
                .ExecuteAsync(scenario.InvoiceKey, "tester"));

        Assert.StartsWith("A data do documento (02/10/2026) precisa ser a de hoje", ex.Message);
        Assert.Equal(0, reservation.Calls);
    }

    [Fact]
    public async Task Retry_after_the_environment_changed_without_a_signed_xml_reserves_a_new_number()
    {
        // A tentativa anterior reservou o número em homologação e parou na validação local
        // (nenhum XML assinado gravado); depois a filial passou para produção.
        var scenario = await NfeTestSeed.SeedAsync();
        var invoice = await scenario.Db.Context.SalesInvoices.SingleAsync();
        invoice.NfeStatus = NfeStatus.Rejected;
        invoice.NfeEnvironment = NfeEnvironment.Homologation;
        invoice.NfeRandomCode = "12345678";
        invoice.TaxDocumentNumber = "000000001";
        invoice.TaxDocumentSeries = "1";
        (await scenario.Db.Context.BranchNfeSettings.SingleAsync()).Environment = NfeEnvironment.Production;
        await scenario.Db.SaveChangesAsync();
        var sefaz = new FakeNfeSefazClient();
        sefaz.AuthorizeResponses.Enqueue(key => FakeNfeSefazClient.Authorized(key));
        var reservation = new FakeNfeNumberReservationService(next: 7);

        await Issue(scenario, sefaz, new RecordingConfirmService(scenario.Db), reservation: reservation)
            .ExecuteAsync(scenario.InvoiceKey, "tester");

        var reloaded = await ReloadAsync(scenario);
        Assert.Equal(1, reservation.Calls);
        Assert.Equal("000000007", reloaded.TaxDocumentNumber);
        Assert.NotEqual("12345678", reloaded.NfeRandomCode);
        Assert.Equal(NfeEnvironment.Production, reloaded.NfeEnvironment);
    }

    [Fact]
    public async Task Denied_nfe_is_archived_as_a_denied_xml()
    {
        var scenario = await NfeTestSeed.SeedAsync();
        var sefaz = new FakeNfeSefazClient();
        sefaz.AuthorizeResponses.Enqueue(key => FakeNfeSefazClient.Authorized(key, status: 110));
        var confirm = new RecordingConfirmService(scenario.Db);

        var outcome = await Issue(scenario, sefaz, confirm).ExecuteAsync(scenario.InvoiceKey, "tester");

        Assert.Equal(NfeStatus.Denied, outcome.NfeStatus);
        Assert.Equal(0, confirm.Calls);
        var xmls = await scenario.Db.Context.SalesInvoiceNfeXmls.AsNoTracking().ToListAsync();
        Assert.Contains(xmls, x => x.Kind == NfeXmlKind.Denied && x.Xml.Contains("<nfeProc") && x.Xml.Contains("<protNFe"));
        Assert.DoesNotContain(xmls, x => x.Kind == NfeXmlKind.Authorized);
    }

    [Fact]
    public async Task Item_numbers_are_saved_with_the_signed_xml()
    {
        var scenario = await NfeTestSeed.SeedAsync();
        var sefaz = new FakeNfeSefazClient();
        sefaz.AuthorizeResponses.Enqueue(key => FakeNfeSefazClient.Authorized(key));

        await Issue(scenario, sefaz, new RecordingConfirmService(scenario.Db)).ExecuteAsync(scenario.InvoiceKey, "tester");

        var item = await TestDb.CreateUnitOfWork(scenario.DatabaseName).Context.SalesInvoicesItems.AsNoTracking()
            .SingleAsync(i => i.SalesInvoiceKey == scenario.InvoiceKey);
        Assert.Equal(1, item.NfeItemNumber);
    }

    [Fact]
    public async Task Line_charges_are_issued_with_the_grand_total()
    {
        var scenario = await NfeTestSeed.SeedAsync();
        var item = await scenario.Db.Context.SalesInvoicesItems.SingleAsync();
        (item.FreightValue, item.InsuranceValue, item.DiscountValue, item.OtherExpensesValue) = (1000m, 100m, 500m, 400m);
        await scenario.Db.SaveChangesAsync();
        var sefaz = new FakeNfeSefazClient();
        sefaz.AuthorizeResponses.Enqueue(key => FakeNfeSefazClient.Authorized(key));

        var outcome = await Issue(scenario, sefaz, new RecordingConfirmService(scenario.Db)).ExecuteAsync(scenario.InvoiceKey, "tester");

        Assert.Equal(NfeStatus.Authorized, outcome.NfeStatus);
        XNamespace ns = "http://www.portalfiscal.inf.br/nfe";
        decimal N(XElement? e) => decimal.Parse(e!.Value, CultureInfo.InvariantCulture);
        var xml = XDocument.Parse(Assert.Single(sefaz.Sent).Xml);
        var prod = xml.Descendants(ns + "det").Single().Element(ns + "prod")!;
        Assert.Equal((1000m, 100m, 500m, 400m),
            (N(prod.Element(ns + "vFrete")), N(prod.Element(ns + "vSeg")), N(prod.Element(ns + "vDesc")), N(prod.Element(ns + "vOutro"))));
        var total = xml.Descendants(ns + "ICMSTot").Single();
        Assert.Equal((60000m, 61000m), (N(total.Element(ns + "vProd")), N(total.Element(ns + "vNF"))));
        var fat = xml.Descendants(ns + "fat").Single();
        Assert.Equal((61000m, 61000m), (N(fat.Element(ns + "vOrig")), N(fat.Element(ns + "vLiq"))));
        Assert.Equal(61000m, xml.Descendants(ns + "vDup").Sum(N));
        Assert.Equal(61000m, N(xml.Descendants(ns + "vPag").Single()));
    }
}
