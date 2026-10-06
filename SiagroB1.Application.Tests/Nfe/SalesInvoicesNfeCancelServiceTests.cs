using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.SalesContracts;
using SiagroB1.Application.Services.SalesInvoices;
using SiagroB1.Application.Services.SalesShipmentReleases;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Fiscal.Nfe;
using SiagroB1.Infra;
using static SiagroB1.Application.Tests.Support.NfeCancelTestServices;

namespace SiagroB1.Application.Tests.Nfe;

/// <summary>Cancelamento da NF-e do documento de saída (spec 2026-10-05 §7.2–§7.5).</summary>
public class SalesInvoicesNfeCancelServiceTests
{
    private const string Reason = "Venda desfeita a pedido do cliente";

    /// <summary>Cancelamento que estoura DEPOIS de mexer no documento — prova que a falha não grava pela metade.</summary>
    private sealed class FailingCancel(UnitOfWork db) : SalesInvoicesCancelService(db,
        new SalesShipmentReleasesRecalculateShippedService(db.Context), new SalesContractsAllocationDeleteForInvoiceService(db),
        new ShipmentLoadsBalanceHookService(db.Context, new ShipmentLoadsMovementLogService(db.Context)),
        NullLogger<SalesInvoicesCancelService>.Instance)
    {
        public override async Task CancelAfterNfeAsync(Guid key, string userName)
        {
            (await db.Context.SalesInvoices.SingleAsync(x => x.Key == key)).InvoiceStatus = InvoiceStatus.Cancelled;
            throw new ApplicationException("Liberação travada por outro usuário.");
        }
    }

    private static async Task<(NfeScenario Scenario, FakeNfeSefazClient Sefaz)> AuthorizedAsync()
    {
        var scenario = await NfeTestSeed.SeedAsync();
        await AuthorizeSaleAsync(scenario.Db, scenario.InvoiceKey);
        return (scenario, new FakeNfeSefazClient());
    }

    [Fact]
    public async Task Registered_cancellation_cancels_the_nfe_and_the_document()
    {
        var (scenario, sefaz) = await AuthorizedAsync();
        sefaz.CancelResponses.Enqueue(r => FakeNfeSefazClient.CancellationRegistered(r.AccessKey));

        var outcome = await SalesCancel(scenario.Db, sefaz).ExecuteAsync(scenario.InvoiceKey, Reason, "tester");

        Assert.Equal(NfeStatus.Cancelled, outcome.NfeStatus);
        Assert.Equal(InvoiceStatus.Cancelled, outcome.InvoiceStatus);
        var saved = await scenario.Db.Context.SalesInvoices.AsNoTracking().SingleAsync();
        Assert.Equal(FakeNfeSefazClient.CancellationProtocol, saved.NfeCancellationProtocol);
        Assert.Equal(FakeNfeSefazClient.CancelledAt.DateTime, saved.NfeCancelledAt);
        Assert.Equal(Reason, saved.NfeCancellationReason);
        Assert.Equal("135", saved.NfeStatusCode);
        Assert.Null(saved.NfeCancellationError);
        Assert.Equal("tester", saved.CanceledBy);
        Assert.Equal(AccessKey, saved.ChaveNFe);
        Assert.Equal(AuthorizationProtocol, saved.NfeProtocol);
        var xml = await scenario.Db.Context.SalesInvoiceNfeXmls.AsNoTracking().SingleAsync(x => x.Kind == NfeXmlKind.CancellationEvent);
        Assert.Contains("<procEventoNFe", xml.Xml);
    }

    [Fact]
    public async Task Request_carries_key_protocol_issuer_and_brasilia_time()
    {
        var (scenario, sefaz) = await AuthorizedAsync();
        sefaz.CancelResponses.Enqueue(r => FakeNfeSefazClient.CancellationRegistered(r.AccessKey));

        await SalesCancel(scenario.Db, sefaz).ExecuteAsync(scenario.InvoiceKey, Reason, "tester");

        var request = Assert.Single(sefaz.CancelRequests);
        Assert.Equal(AccessKey, request.AccessKey);
        Assert.Equal(AuthorizationProtocol, request.AuthorizationProtocol);
        Assert.Equal("12345678000195", request.IssuerDocument);
        Assert.Equal(Reason, request.Justification);
        Assert.Equal(TimeSpan.FromHours(-3), request.EventAt.Offset);
        Assert.Equal(NfeEnvironment.Homologation, Assert.Single(sefaz.CancelSettings).Environment);
    }

    [Fact]
    public async Task Out_of_time_registration_155_also_cancels()
    {
        var (scenario, sefaz) = await AuthorizedAsync();
        sefaz.CancelResponses.Enqueue(r => FakeNfeSefazClient.CancellationRegistered(r.AccessKey, 155));

        var outcome = await SalesCancel(scenario.Db, sefaz).ExecuteAsync(scenario.InvoiceKey, Reason, "tester");

        Assert.Equal(NfeStatus.Cancelled, outcome.NfeStatus);
    }

    [Theory]
    [InlineData("curta demais")]
    [InlineData("")]
    [InlineData(null)]
    public async Task Short_justification_is_refused_without_calling_sefaz(string? justification)
    {
        var (scenario, sefaz) = await AuthorizedAsync();

        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            SalesCancel(scenario.Db, sefaz).ExecuteAsync(scenario.InvoiceKey, justification, "tester"));

        Assert.Equal("A justificativa deve ter entre 15 e 255 caracteres.", ex.Message);
        Assert.Empty(sefaz.CancelRequests);
    }

    [Fact]
    public async Task Long_justification_is_refused()
    {
        var (scenario, sefaz) = await AuthorizedAsync();

        await Assert.ThrowsAsync<DefaultException>(() =>
            SalesCancel(scenario.Db, sefaz).ExecuteAsync(scenario.InvoiceKey, new string('x', 256), "tester"));

        Assert.Empty(sefaz.CancelRequests);
    }

    [Fact]
    public async Task Justification_is_trimmed_before_counting_and_saving()
    {
        var (scenario, sefaz) = await AuthorizedAsync();
        sefaz.CancelResponses.Enqueue(r => FakeNfeSefazClient.CancellationRegistered(r.AccessKey));

        await SalesCancel(scenario.Db, sefaz).ExecuteAsync(scenario.InvoiceKey, $"   {Reason}  ", "tester");

        Assert.Equal(Reason, Assert.Single(sefaz.CancelRequests).Justification);
        Assert.Equal(Reason, (await scenario.Db.Context.SalesInvoices.AsNoTracking().SingleAsync()).NfeCancellationReason);

        var (other, sefaz2) = await AuthorizedAsync();
        await Assert.ThrowsAsync<DefaultException>(() =>
            SalesCancel(other.Db, sefaz2).ExecuteAsync(other.InvoiceKey, "   curta     ", "tester"));
    }

    [Theory]
    [InlineData(NfeStatus.None, "Só NF-e autorizada pode ser cancelada.")]
    [InlineData(NfeStatus.Processing, "Só NF-e autorizada pode ser cancelada.")]
    [InlineData(NfeStatus.Rejected, "Só NF-e autorizada pode ser cancelada.")]
    [InlineData(NfeStatus.Cancelled, "A NF-e deste documento já foi cancelada: use Concluir cancelamento.")]
    public async Task Only_authorized_nfe_is_cancelled(NfeStatus status, string message)
    {
        var (scenario, sefaz) = await AuthorizedAsync();
        (await scenario.Db.Context.SalesInvoices.SingleAsync()).NfeStatus = status;
        await scenario.Db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            SalesCancel(scenario.Db, sefaz).ExecuteAsync(scenario.InvoiceKey, Reason, "tester"));

        Assert.Equal(message, ex.Message);
        Assert.Empty(sefaz.CancelRequests);
    }

    [Fact]
    public async Task Local_rule_refuses_before_calling_sefaz()
    {
        var returnScenario = await NfeReturnTestSeed.SeedAsync();
        await NfeReturnTestSeed.CreateReturnAsync(returnScenario, 10m);
        await AuthorizeSaleAsync(returnScenario.Sale.Db, returnScenario.Sale.InvoiceKey);
        var sefaz = new FakeNfeSefazClient();

        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            SalesCancel(returnScenario.Sale.Db, sefaz).ExecuteAsync(returnScenario.Sale.InvoiceKey, Reason, "tester"));

        Assert.Equal("Documento de saída possui retorno.", ex.Message);
        Assert.Empty(sefaz.CancelRequests);
    }

    [Fact]
    public async Task Sefaz_rejection_changes_nothing()
    {
        var (scenario, sefaz) = await AuthorizedAsync();
        sefaz.CancelResponses.Enqueue(_ => new NfeEventResult(501, "Rejeição: Prazo de cancelamento superior ao previsto na Legislação"));

        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            SalesCancel(scenario.Db, sefaz).ExecuteAsync(scenario.InvoiceKey, Reason, "tester"));

        Assert.Equal("Cancelamento recusado pela SEFAZ: 501 - Rejeição: Prazo de cancelamento superior ao previsto na Legislação", ex.Message);
        var saved = await scenario.Db.Context.SalesInvoices.AsNoTracking().SingleAsync();
        Assert.Equal(NfeStatus.Authorized, saved.NfeStatus);
        Assert.Equal(InvoiceStatus.Confirmed, saved.InvoiceStatus);
        Assert.Equal("100", saved.NfeStatusCode);
    }

    [Fact]
    public async Task Communication_failure_changes_nothing()
    {
        var (scenario, sefaz) = await AuthorizedAsync();
        sefaz.CancelResponses.Enqueue(_ => throw new NfeCommunicationException("Tempo esgotado."));

        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            SalesCancel(scenario.Db, sefaz).ExecuteAsync(scenario.InvoiceKey, Reason, "tester"));

        Assert.Equal("Sem resposta da SEFAZ no cancelamento: use Consultar situação antes de tentar de novo.", ex.Message);
        Assert.Equal(NfeStatus.Authorized, (await scenario.Db.Context.SalesInvoices.AsNoTracking().SingleAsync()).NfeStatus);
    }

    [Fact]
    public async Task Duplicate_event_is_resolved_by_the_consult()
    {
        var (scenario, sefaz) = await AuthorizedAsync();
        sefaz.CancelResponses.Enqueue(_ => new NfeEventResult(573, "Rejeição: Duplicidade de evento"));
        sefaz.ConsultResponses.Enqueue(key => FakeNfeSefazClient.ConsultCancelled(key));

        var outcome = await SalesCancel(scenario.Db, sefaz).ExecuteAsync(scenario.InvoiceKey, Reason, "tester");

        Assert.Equal(NfeStatus.Cancelled, outcome.NfeStatus);
        Assert.Equal(InvoiceStatus.Cancelled, outcome.InvoiceStatus);
        var saved = await scenario.Db.Context.SalesInvoices.AsNoTracking().SingleAsync();
        Assert.Equal("Cancelada direto no portal da SEFAZ", saved.NfeCancellationReason);
    }

    [Fact]
    public async Task Duplicate_event_with_consult_timeout_is_a_default_exception_and_changes_nothing()
    {
        var (scenario, sefaz) = await AuthorizedAsync();
        sefaz.CancelResponses.Enqueue(_ => new NfeEventResult(573, "Rejeição: Duplicidade de evento"));
        sefaz.ConsultResponses.Enqueue(_ => throw new NfeCommunicationException("Tempo esgotado."));

        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            SalesCancel(scenario.Db, sefaz).ExecuteAsync(scenario.InvoiceKey, Reason, "tester"));

        Assert.Equal("Sem resposta da SEFAZ no cancelamento: use Consultar situação antes de tentar de novo.", ex.Message);
        Assert.Equal(NfeStatus.Authorized, (await scenario.Db.Context.SalesInvoices.AsNoTracking().SingleAsync()).NfeStatus);
    }

    [Fact]
    public async Task Duplicate_event_not_confirmed_by_the_consult_changes_nothing()
    {
        var (scenario, sefaz) = await AuthorizedAsync();
        sefaz.CancelResponses.Enqueue(_ => new NfeEventResult(573, "Rejeição: Duplicidade de evento"));
        sefaz.ConsultResponses.Enqueue(key => FakeNfeSefazClient.Authorized(key));

        await Assert.ThrowsAsync<DefaultException>(() =>
            SalesCancel(scenario.Db, sefaz).ExecuteAsync(scenario.InvoiceKey, Reason, "tester"));

        Assert.Equal(NfeStatus.Authorized, (await scenario.Db.Context.SalesInvoices.AsNoTracking().SingleAsync()).NfeStatus);
    }

    [Fact]
    public async Task Local_failure_after_sefaz_keeps_the_nfe_cancelled_and_records_the_error()
    {
        var (scenario, sefaz) = await AuthorizedAsync();
        sefaz.CancelResponses.Enqueue(r => FakeNfeSefazClient.CancellationRegistered(r.AccessKey));

        var outcome = await SalesCancel(scenario.Db, sefaz, new FailingCancel(scenario.Db))
            .ExecuteAsync(scenario.InvoiceKey, Reason, "tester");

        Assert.Equal(NfeStatus.Cancelled, outcome.NfeStatus);
        Assert.Equal("Liberação travada por outro usuário.", outcome.CancellationError);
        var saved = await scenario.Db.Context.SalesInvoices.AsNoTracking().SingleAsync();
        Assert.Equal(NfeStatus.Cancelled, saved.NfeStatus);
        Assert.Equal(InvoiceStatus.Confirmed, saved.InvoiceStatus);
        Assert.Equal("Liberação travada por outro usuário.", saved.NfeCancellationError);
        Assert.Equal(FakeNfeSefazClient.CancellationProtocol, saved.NfeCancellationProtocol);
    }

    [Fact]
    public async Task Complete_cancellation_runs_only_the_local_phase()
    {
        var (scenario, sefaz) = await AuthorizedAsync();
        sefaz.CancelResponses.Enqueue(r => FakeNfeSefazClient.CancellationRegistered(r.AccessKey));
        await SalesCancel(scenario.Db, sefaz, new FailingCancel(scenario.Db)).ExecuteAsync(scenario.InvoiceKey, Reason, "tester");
        scenario.Db.Context.ChangeTracker.Clear();

        var outcome = await SalesComplete(scenario.Db).ExecuteAsync(scenario.InvoiceKey, "tester");

        Assert.Equal(InvoiceStatus.Cancelled, outcome.InvoiceStatus);
        Assert.Null(outcome.CancellationError);
        Assert.Single(sefaz.CancelRequests);
        Assert.Null((await scenario.Db.Context.SalesInvoices.AsNoTracking().SingleAsync()).NfeCancellationError);
    }

    [Fact]
    public async Task Complete_cancellation_requires_cancelled_nfe_and_active_document()
    {
        var (scenario, _) = await AuthorizedAsync();

        var ex = await Assert.ThrowsAsync<DefaultException>(() => SalesComplete(scenario.Db).ExecuteAsync(scenario.InvoiceKey, "tester"));

        Assert.Equal("Só documento com NF-e cancelada e ainda ativo tem cancelamento a concluir.", ex.Message);
    }

    [Fact]
    public async Task Cancel_while_an_emission_lock_is_held_is_refused_without_calling_sefaz()
    {
        var (scenario, sefaz) = await AuthorizedAsync();
        var reservation = new FakeNfeNumberReservationService { EmissionBusy = true };

        await Assert.ThrowsAsync<DefaultException>(() =>
            SalesCancel(scenario.Db, sefaz, reservation: reservation).ExecuteAsync(scenario.InvoiceKey, Reason, "tester"));

        Assert.Empty(sefaz.CancelRequests);
    }

    [Fact]
    public async Task Cancelling_an_authorized_sales_return_restores_the_origin()
    {
        var returnScenario = await NfeReturnTestSeed.SeedAsync();
        var created = await NfeReturnTestSeed.CreateReturnAsync(returnScenario, 10m);
        var db = returnScenario.Sale.Db;
        var origin = await db.Context.SalesInvoices.SingleAsync(x => x.Key == returnScenario.Sale.InvoiceKey);
        origin.InvoiceStatus = InvoiceStatus.Returned;
        await db.SaveChangesAsync();
        await AuthorizeSaleAsync(db, created.Key);
        var sefaz = new FakeNfeSefazClient();
        sefaz.CancelResponses.Enqueue(r => FakeNfeSefazClient.CancellationRegistered(r.AccessKey));

        var outcome = await SalesCancel(db, sefaz).ExecuteAsync(created.Key, Reason, "tester");

        Assert.Equal(InvoiceStatus.Cancelled, outcome.InvoiceStatus);
        Assert.Equal(InvoiceStatus.Confirmed,
            (await db.Context.SalesInvoices.AsNoTracking().SingleAsync(x => x.Key == returnScenario.Sale.InvoiceKey)).InvoiceStatus);
    }

    [Fact]
    public async Task Cancelling_a_confirmed_nfe_return_recalculates_the_origin_returned_quantity()
    {
        var returnScenario = await NfeReturnTestSeed.SeedAsync();
        var created = await NfeReturnTestSeed.CreateReturnAsync(returnScenario, 10m);
        var db = returnScenario.Sale.Db;
        var originKey = returnScenario.Sale.InvoiceKey;

        // Pré-condição: devolução confirmada → a origem carrega a quantidade devolvida.
        await AuthorizeSaleAsync(db, created.Key);
        await SalesInvoicesRecalculateReturnedService.RecalculateAsync(db.Context, originKey);
        await db.SaveChangesAsync();
        var before = await db.Context.SalesInvoices.AsNoTracking().Include(x => x.Items).SingleAsync(x => x.Key == originKey);
        Assert.True(before.ReturnedQuantity > 0);
        Assert.All(before.Items, i => Assert.True(i.ReturnedQuantity > 0));

        var sefaz = new FakeNfeSefazClient();
        sefaz.CancelResponses.Enqueue(r => FakeNfeSefazClient.CancellationRegistered(r.AccessKey));

        var outcome = await SalesCancel(db, sefaz).ExecuteAsync(created.Key, Reason, "tester");

        Assert.Equal(InvoiceStatus.Cancelled, outcome.InvoiceStatus);
        var origin = await db.Context.SalesInvoices.AsNoTracking().Include(x => x.Items).SingleAsync(x => x.Key == originKey);
        Assert.Equal(0m, origin.ReturnedQuantity);
        Assert.All(origin.Items, i => Assert.Equal(0m, i.ReturnedQuantity));
    }

    [Fact]
    public async Task Pending_document_with_authorized_nfe_is_cancelled_too()
    {
        var scenario = await NfeTestSeed.SeedAsync();
        await AuthorizeSaleAsync(scenario.Db, scenario.InvoiceKey, InvoiceStatus.Pending);
        var sefaz = new FakeNfeSefazClient();
        sefaz.CancelResponses.Enqueue(r => FakeNfeSefazClient.CancellationRegistered(r.AccessKey));

        var outcome = await SalesCancel(scenario.Db, sefaz).ExecuteAsync(scenario.InvoiceKey, Reason, "tester");

        Assert.Equal(InvoiceStatus.Cancelled, outcome.InvoiceStatus);
    }

    [Fact]
    public async Task Duplicate_event_resolved_by_an_out_of_time_consult_151_cancels()
    {
        var (scenario, sefaz) = await AuthorizedAsync();
        sefaz.CancelResponses.Enqueue(_ => new NfeEventResult(573, "Rejeição: Duplicidade de evento"));
        sefaz.ConsultResponses.Enqueue(key => FakeNfeSefazClient.ConsultCancelled(key, status: 151));

        var outcome = await SalesCancel(scenario.Db, sefaz).ExecuteAsync(scenario.InvoiceKey, Reason, "tester");

        Assert.Equal(NfeStatus.Cancelled, outcome.NfeStatus);
        Assert.Equal(InvoiceStatus.Cancelled, outcome.InvoiceStatus);
        Assert.Equal(FakeNfeSefazClient.CancellationProtocol,
            (await scenario.Db.Context.SalesInvoices.AsNoTracking().SingleAsync()).NfeCancellationProtocol);
    }

    [Fact]
    public async Task Duplicate_event_resolved_by_a_consult_without_the_event_keeps_the_typed_justification()
    {
        var (scenario, sefaz) = await AuthorizedAsync();
        sefaz.CancelResponses.Enqueue(_ => new NfeEventResult(573, "Rejeição: Duplicidade de evento"));
        sefaz.ConsultResponses.Enqueue(key => FakeNfeSefazClient.ConsultCancelled(key, withEvent: false));

        await SalesCancel(scenario.Db, sefaz).ExecuteAsync(scenario.InvoiceKey, Reason, "tester");

        var saved = await scenario.Db.Context.SalesInvoices.AsNoTracking().SingleAsync();
        Assert.Equal(NfeStatus.Cancelled, saved.NfeStatus);
        Assert.Equal(Reason, saved.NfeCancellationReason);
    }

    [Fact]
    public async Task Failure_saving_the_registered_cancellation_asks_to_consult_and_keeps_the_nfe_authorized()
    {
        var (scenario, sefaz) = await AuthorizedAsync();
        sefaz.CancelResponses.Enqueue(r => FakeNfeSefazClient.CancellationRegistered(r.AccessKey));
        var failingDb = TestDb.CreateUnitOfWork(scenario.DatabaseName, new ThrowOnSaveInterceptor(
            e => e.Entity is Domain.Entities.SalesInvoiceNfeXml && e.State == EntityState.Added));

        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            SalesCancel(failingDb, sefaz).ExecuteAsync(scenario.InvoiceKey, Reason, "tester"));

        Assert.Equal("A SEFAZ pode ter registrado o cancelamento, mas ele não foi gravado: use Consultar situação.", ex.Message);
        var saved = await scenario.Db.Context.SalesInvoices.AsNoTracking().SingleAsync();
        Assert.Equal(NfeStatus.Authorized, saved.NfeStatus);
        Assert.Equal(InvoiceStatus.Confirmed, saved.InvoiceStatus);
    }

    [Fact]
    public async Task Unexpected_failure_sending_the_event_asks_to_consult_and_changes_nothing()
    {
        var (scenario, sefaz) = await AuthorizedAsync();
        sefaz.CancelResponses.Enqueue(_ => throw new InvalidOperationException("Falha de TLS."));

        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            SalesCancel(scenario.Db, sefaz).ExecuteAsync(scenario.InvoiceKey, Reason, "tester"));

        // A falha pode ser local (XSD ausente, certificado): o detalhe técnico vai junto da orientação.
        Assert.StartsWith("Sem resposta da SEFAZ no cancelamento: use Consultar situação antes de tentar de novo.", ex.Message);
        Assert.Contains("detalhe técnico", ex.Message);
        Assert.Equal(NfeStatus.Authorized, (await scenario.Db.Context.SalesInvoices.AsNoTracking().SingleAsync()).NfeStatus);
    }
}
