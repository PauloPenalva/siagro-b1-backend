using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.Nfe;
using SiagroB1.Application.Services.NfeEmail;
using SiagroB1.Application.Services.Taxes;
using SiagroB1.Application.Tests.Nfe;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Fiscal.Nfe;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.NfeEmail;

/// <summary>Gatilhos da fila de e-mail da NF-e: só na TRANSIÇÃO para Autorizada/Cancelada, uma linha automática por evento.</summary>
public class NfeEmailTriggerTests
{
    private static async Task EnableAsync(UnitOfWork db, bool autoSend = true)
    {
        db.Context.EmailSettings.Add(new EmailSettings
        {
            Key = 1, Host = "smtp.exemplo.com.br", FromAddress = "nfe@ceagui.com.br", AutoSendEnabled = autoSend,
            UpdatedBy = "tester",
        });
        await db.SaveChangesAsync();
    }

    private static NfeEmailOutboxService Outbox(UnitOfWork db, string erp = "STANDALONE") =>
        new(db, NfeTestSeed.Config(erp), NullLogger<NfeEmailOutboxService>.Instance);

    private static SalesInvoiceNfeResultHandler SalesResultHandler(UnitOfWork db, string erp = "STANDALONE") =>
        new(db, new RecordingConfirmService(db), NullLogger<SalesInvoiceNfeResultHandler>.Instance, Outbox(db, erp));

    private static SalesInvoicesNfeIssueService SalesIssue(NfeScenario scenario, FakeNfeSefazClient sefaz, IConfiguration config,
        SalesInvoiceNfeResultHandler handler)
    {
        var options = new NfeOptions(config);
        return new SalesInvoicesNfeIssueService(
            scenario.Db, new TaxCalculationGate(scenario.Db, config), new NfeReadinessValidator(scenario.Db, options),
            new BranchNfeSettingsService(scenario.Db, options, sefaz), new FakeNfeNumberReservationService(), sefaz,
            handler, options, NullLogger<SalesInvoicesNfeIssueService>.Instance, NfeTestSeed.Clock,
            NfeIssueInputAssembler.BrasiliaZone);
    }

    private static async Task<NfeScenario> AuthorizedSaleAsync(SalesInvoiceNfeResultHandler? handler = null)
    {
        var scenario = await NfeTestSeed.SeedAsync(InvoiceStatus.Confirmed);
        await EnableAsync(scenario.Db);
        var sefaz = new FakeNfeSefazClient();
        sefaz.AuthorizeResponses.Enqueue(key => FakeNfeSefazClient.Authorized(key));
        await SalesIssue(scenario, sefaz, NfeTestSeed.Config(), handler ?? SalesResultHandler(scenario.Db))
            .ExecuteAsync(scenario.InvoiceKey, "tester");
        return scenario;
    }

    private static Task<List<NfeEmailMessage>> RowsAsync(UnitOfWork db) =>
        db.Context.NfeEmailMessages.AsNoTracking().ToListAsync();

    private static SalesInvoiceNfeCancellationHandler CancelHandler(UnitOfWork db, string erp = "STANDALONE") =>
        NfeCancelTestServices.SalesHandler(db, emailOutbox: Outbox(db, erp));

    [Fact]
    public async Task Authorization_enqueues_one_automatic_email()
    {
        var scenario = await AuthorizedSaleAsync();

        var row = Assert.Single(await RowsAsync(scenario.Db));
        Assert.Equal(NfeEmailDocumentType.Sales, row.DocumentType);
        Assert.Equal(scenario.InvoiceKey, row.DocumentKey);
        Assert.Equal(NfeEmailEventType.Authorized, row.EventType);
        Assert.Equal(NfeEmailTrigger.Automatic, row.Trigger);
        Assert.Equal(NfeEmailStatus.Pending, row.Status);
        Assert.Equal("tester", row.CreatedBy);
    }

    [Fact]
    public async Task Consult_on_already_authorized_does_not_enqueue()
    {
        var scenario = await AuthorizedSaleAsync();
        scenario.Db.Context.NfeEmailMessages.RemoveRange(scenario.Db.Context.NfeEmailMessages);
        await scenario.Db.SaveChangesAsync();
        var invoice = await scenario.Db.Context.SalesInvoices.SingleAsync(i => i.Key == scenario.InvoiceKey);
        var signed = await new SalesInvoiceNfeStore(scenario.Db).SignedXmlsAsync(scenario.InvoiceKey);

        // Reaplicar o retorno "autorizada" numa nota que já está autorizada não é transição.
        var outcome = await SalesResultHandler(scenario.Db)
            .ApplyConsultAsync(invoice, signed, FakeNfeSefazClient.Authorized(invoice.ChaveNFe!, digVal: NfeProcComposer.SignedDigest(signed[0])), "tester");

        Assert.Equal(NfeStatus.Authorized, outcome.NfeStatus);
        Assert.Empty(await RowsAsync(scenario.Db));
    }

    [Fact]
    public async Task Cancellation_enqueues_cancelled_email()
    {
        var scenario = await NfeTestSeed.SeedAsync();
        await EnableAsync(scenario.Db);
        await NfeCancelTestServices.AuthorizeSaleAsync(scenario.Db, scenario.InvoiceKey);
        var invoice = await scenario.Db.Context.SalesInvoices.SingleAsync(i => i.Key == scenario.InvoiceKey);

        await CancelHandler(scenario.Db).ApplyRegisteredAsync(
            invoice, FakeNfeSefazClient.CancellationRegistered(invoice.ChaveNFe!), "Venda desfeita a pedido do cliente", "tester");

        var row = Assert.Single(await RowsAsync(scenario.Db));
        Assert.Equal(NfeEmailEventType.Cancelled, row.EventType);
        Assert.Equal(NfeEmailDocumentType.Sales, row.DocumentType);
        Assert.Equal(scenario.InvoiceKey, row.DocumentKey);
    }

    [Fact]
    public async Task Consult_cancellation_outside_siagro_enqueues()
    {
        var scenario = await NfeTestSeed.SeedAsync();
        await EnableAsync(scenario.Db);
        await NfeCancelTestServices.AuthorizeSaleAsync(scenario.Db, scenario.InvoiceKey);
        var sefaz = new FakeNfeSefazClient();
        sefaz.ConsultResponses.Enqueue(key => FakeNfeSefazClient.ConsultCancelled(key, withEvent: false));
        var options = new NfeOptions(NfeTestSeed.Config());
        var consult = new SalesInvoicesNfeConsultService(
            scenario.Db, new BranchNfeSettingsService(scenario.Db, options, sefaz), sefaz, SalesResultHandler(scenario.Db),
            CancelHandler(scenario.Db), new FakeNfeNumberReservationService(),
            NullLogger<SalesInvoicesNfeConsultService>.Instance);

        var outcome = await consult.ExecuteAsync(scenario.InvoiceKey, "tester");

        Assert.Equal(NfeStatus.Cancelled, outcome.NfeStatus);
        var row = Assert.Single(await RowsAsync(scenario.Db));
        Assert.Equal(NfeEmailEventType.Cancelled, row.EventType);
    }

    [Fact]
    public async Task Disabled_settings_do_not_enqueue()
    {
        var scenario = await NfeTestSeed.SeedAsync(InvoiceStatus.Confirmed);
        await EnableAsync(scenario.Db, autoSend: false);
        var sefaz = new FakeNfeSefazClient();
        sefaz.AuthorizeResponses.Enqueue(key => FakeNfeSefazClient.Authorized(key));

        var outcome = await SalesIssue(scenario, sefaz, NfeTestSeed.Config(), SalesResultHandler(scenario.Db))
            .ExecuteAsync(scenario.InvoiceKey, "tester");

        Assert.Equal(NfeStatus.Authorized, outcome.NfeStatus);
        Assert.Empty(await RowsAsync(scenario.Db));
    }

    [Fact]
    public async Task Missing_settings_do_not_enqueue()
    {
        var scenario = await NfeTestSeed.SeedAsync();
        await NfeCancelTestServices.AuthorizeSaleAsync(scenario.Db, scenario.InvoiceKey);
        var invoice = await scenario.Db.Context.SalesInvoices.SingleAsync(i => i.Key == scenario.InvoiceKey);

        var outcome = await CancelHandler(scenario.Db).ApplyRegisteredAsync(
            invoice, FakeNfeSefazClient.CancellationRegistered(invoice.ChaveNFe!), "Venda desfeita a pedido do cliente", "tester");

        Assert.Equal(NfeStatus.Cancelled, outcome.NfeStatus);
        Assert.Empty(await RowsAsync(scenario.Db));
    }

    [Fact]
    public async Task Sapb1_mode_does_not_enqueue()
    {
        var scenario = await NfeTestSeed.SeedAsync();
        await EnableAsync(scenario.Db);
        await NfeCancelTestServices.AuthorizeSaleAsync(scenario.Db, scenario.InvoiceKey);
        var invoice = await scenario.Db.Context.SalesInvoices.SingleAsync(i => i.Key == scenario.InvoiceKey);

        var outcome = await CancelHandler(scenario.Db, erp: "SAPB1").ApplyRegisteredAsync(
            invoice, FakeNfeSefazClient.CancellationRegistered(invoice.ChaveNFe!), "Venda desfeita a pedido do cliente", "tester");

        Assert.Equal(NfeStatus.Cancelled, outcome.NfeStatus);
        Assert.Empty(await RowsAsync(scenario.Db));
    }

    [Fact]
    public async Task Purchase_own_authorization_enqueues_purchase_type()
    {
        var scenario = await PurchaseNfeTestSeed.SeedAsync();
        await EnableAsync(scenario.Db);
        var sefaz = new FakeNfeSefazClient();
        sefaz.AuthorizeResponses.Enqueue(key => FakeNfeSefazClient.Authorized(key));
        var handler = new PurchaseInvoiceNfeResultHandler(scenario.Db, TaxTestServices.PurchaseConfirm(scenario.Db),
            NullLogger<PurchaseInvoiceNfeResultHandler>.Instance, Outbox(scenario.Db));

        var outcome = await PurchaseInvoicesNfeIssueServiceTests.Issue(scenario, sefaz, handler: handler)
            .ExecuteAsync(scenario.InvoiceKey, "tester");

        Assert.Equal(NfeStatus.Authorized, outcome.NfeStatus);
        var row = Assert.Single(await RowsAsync(scenario.Db));
        Assert.Equal(NfeEmailDocumentType.Purchase, row.DocumentType);
        Assert.Equal(scenario.InvoiceKey, row.DocumentKey);
        Assert.Equal(NfeEmailEventType.Authorized, row.EventType);
    }

    [Fact]
    public async Task Second_apply_does_not_duplicate()
    {
        var scenario = await AuthorizedSaleAsync();
        var outbox = Outbox(scenario.Db);

        // Mesmo chamado de novo (ex.: o handler sem a trava de transição), a linha automática não se repete.
        await outbox.AddAutomaticAsync(NfeEmailDocumentType.Sales, scenario.InvoiceKey, NfeEmailEventType.Authorized, "tester");
        await scenario.Db.SaveChangesAsync();

        Assert.Single(await RowsAsync(scenario.Db));
    }
}
