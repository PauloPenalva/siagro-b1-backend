using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.Nfe;
using SiagroB1.Application.Services.PurchaseInvoices;
using SiagroB1.Application.Services.Taxes;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Application.Tests.Nfe;

/// <summary>"Emitir NF-e" da entrada própria (spec §8.3), com a SEFAZ simulada e o XML assinado de verdade.</summary>
public class PurchaseInvoicesNfeIssueServiceTests
{
    internal static PurchaseInvoiceNfeResultHandler Handler(PurchaseNfeScenario scenario) =>
        new(scenario.Db, TaxTestServices.PurchaseConfirm(scenario.Db),
            NullLogger<PurchaseInvoiceNfeResultHandler>.Instance);

    internal static PurchaseInvoicesNfeIssueService Issue(
        PurchaseNfeScenario scenario, FakeNfeSefazClient sefaz, FakeNfeNumberReservationService? reservation = null)
    {
        var config = NfeTestSeed.Config();
        var options = new NfeOptions(config);

        return new PurchaseInvoicesNfeIssueService(
            scenario.Db, new TaxCalculationGate(scenario.Db, config), new NfeReadinessValidator(scenario.Db, options),
            new BranchNfeSettingsService(scenario.Db, options, sefaz), reservation ?? new FakeNfeNumberReservationService(),
            sefaz, Handler(scenario), options, NullLogger<PurchaseInvoicesNfeIssueService>.Instance,
            NfeTestSeed.Clock, NfeIssueInputAssembler.BrasiliaZone);
    }

    internal static Task<PurchaseInvoice> ReloadAsync(PurchaseNfeScenario scenario, Guid? key = null) =>
        TestDb.CreateUnitOfWork(scenario.DatabaseName).Context.PurchaseInvoices.AsNoTracking().Include(i => i.Items)
            .SingleAsync(i => i.Key == (key ?? scenario.InvoiceKey));

    private static async Task ChangeAsync(PurchaseNfeScenario scenario, Action<PurchaseInvoice> change)
    {
        // No contexto do cenário: é ele que o serviço lê, e o seed deixou a entrada rastreada nele
        // (outro contexto gravaria no banco, mas o serviço continuaria vendo a cópia rastreada).
        var context = scenario.Db.Context;
        var invoice = await context.PurchaseInvoices.Include(i => i.Items).SingleAsync(i => i.Key == scenario.InvoiceKey);
        change(invoice);
        await context.SaveChangesAsync();
    }

    [Fact]
    public async Task Authorized_entry_is_saved_and_the_document_confirmed()
    {
        var scenario = await PurchaseNfeTestSeed.SeedAsync();
        var sefaz = new FakeNfeSefazClient();
        sefaz.AuthorizeResponses.Enqueue(key => FakeNfeSefazClient.Authorized(key));

        var outcome = await Issue(scenario, sefaz).ExecuteAsync(scenario.InvoiceKey, "tester");

        var invoice = await ReloadAsync(scenario);
        Assert.Equal(NfeStatus.Authorized, outcome.NfeStatus);
        Assert.Equal(InvoiceStatus.Confirmed, invoice.InvoiceStatus);
        Assert.Equal("000000001", invoice.TaxDocumentNumber);
        Assert.Equal(44, invoice.ChaveNFe!.Length);
        Assert.Equal(1500m, invoice.TotalDocumentValue);
        Assert.Equal(1, invoice.Items.Single().NfeItemNumber);

        var signed = Assert.Single(sefaz.Sent).Xml;
        Assert.Contains("<tpNF>0</tpNF>", signed);
        Assert.Contains("<finNFe>1</finNFe>", signed);
        // Sem NF-e referenciada no cabeçalho: o campo único saiu (NFref é 1:N no leiaute).
        Assert.DoesNotContain("<NFref>", signed);
        Assert.Contains("<CFOP>1102</CFOP>", signed);
        Assert.Contains("<indFinal>0</indFinal>", signed);
        Assert.Contains("Funrural", signed);

        var xmls = await scenario.Db.Context.PurchaseInvoiceNfeXmls.AsNoTracking().ToListAsync();
        Assert.Contains(xmls, x => x.Kind == NfeXmlKind.Signed);
        Assert.Contains(xmls, x => x.Kind == NfeXmlKind.Authorized && x.Xml.Contains("<protNFe"));
    }

    [Fact]
    public async Task Entry_volume_goes_to_the_vol_group()
    {
        var scenario = await PurchaseNfeTestSeed.SeedAsync();
        await ChangeAsync(scenario, invoice =>
        {
            invoice.VolumeQuantity = 20;
            invoice.VolumeSpecies = " SACO ";
            invoice.VolumeBrand = "CEAGUI";
            invoice.VolumeNumbering = "1 A 20";
        });
        var sefaz = new FakeNfeSefazClient();
        sefaz.AuthorizeResponses.Enqueue(key => FakeNfeSefazClient.Authorized(key));

        await Issue(scenario, sefaz).ExecuteAsync(scenario.InvoiceKey, "tester");

        var signed = Assert.Single(sefaz.Sent).Xml;
        Assert.Contains("<qVol>20</qVol><esp>SACO</esp><marca>CEAGUI</marca><nVol>1 A 20</nVol>", signed);
    }

    [Fact]
    public async Task Entry_and_sale_share_the_branch_number_sequence()
    {
        var scenario = await PurchaseNfeTestSeed.SeedAsync();
        var sefaz = new FakeNfeSefazClient();
        sefaz.AuthorizeResponses.Enqueue(key => FakeNfeSefazClient.Authorized(key));
        sefaz.AuthorizeResponses.Enqueue(key => FakeNfeSefazClient.Authorized(key));
        var reservation = new FakeNfeNumberReservationService();
        var config = NfeTestSeed.Config();
        var options = new NfeOptions(config);
        var saleIssue = new SalesInvoicesNfeIssueService(
            scenario.Db, new TaxCalculationGate(scenario.Db, config), new NfeReadinessValidator(scenario.Db, options),
            new BranchNfeSettingsService(scenario.Db, options, sefaz), reservation, sefaz,
            new SalesInvoiceNfeResultHandler(scenario.Db, new RecordingConfirmService(scenario.Db), NullLogger<SalesInvoiceNfeResultHandler>.Instance),
            options, NullLogger<SalesInvoicesNfeIssueService>.Instance, NfeTestSeed.Clock, NfeIssueInputAssembler.BrasiliaZone);

        await saleIssue.ExecuteAsync(scenario.SaleKey, "tester");
        await Issue(scenario, sefaz, reservation).ExecuteAsync(scenario.InvoiceKey, "tester");

        Assert.Equal("000000002", (await ReloadAsync(scenario)).TaxDocumentNumber);
    }

    [Fact]
    public async Task Rejected_entry_stays_pending_with_the_reason()
    {
        var scenario = await PurchaseNfeTestSeed.SeedAsync();
        var sefaz = new FakeNfeSefazClient();
        sefaz.AuthorizeResponses.Enqueue(_ => FakeNfeSefazClient.Rejected());

        var outcome = await Issue(scenario, sefaz).ExecuteAsync(scenario.InvoiceKey, "tester");

        var invoice = await ReloadAsync(scenario);
        Assert.Equal(NfeStatus.Rejected, outcome.NfeStatus);
        Assert.Equal(InvoiceStatus.Pending, invoice.InvoiceStatus);
        Assert.Equal("209", invoice.NfeStatusCode);
    }

    [Fact]
    public async Task Third_party_document_is_not_issued()
    {
        var scenario = await PurchaseNfeTestSeed.SeedAsync();
        await ChangeAsync(scenario, i => i.IssuerType = DocumentIssuerType.ThirdParty);

        var e = await Assert.ThrowsAsync<DefaultException>(
            () => Issue(scenario, new FakeNfeSefazClient()).ExecuteAsync(scenario.InvoiceKey, "tester"));

        Assert.Equal("Só o documento de emissão própria é emitido como NF-e.", e.Message);
    }

    [Fact]
    public async Task Uncalculated_line_asks_to_save_again()
    {
        // Review Focus 2: entrada criada com a chave desligada e emitida depois de ligá-la.
        var scenario = await PurchaseNfeTestSeed.SeedAsync();
        await ChangeAsync(scenario, i => i.Items.Single().Cfop = null);

        var e = await Assert.ThrowsAsync<DefaultException>(
            () => Issue(scenario, new FakeNfeSefazClient()).ExecuteAsync(scenario.InvoiceKey, "tester"));

        Assert.Equal("O item TRIGO está sem os tributos calculados. Salve o documento para recalcular antes de emitir.", e.Message);
    }

    [Fact]
    public async Task Readiness_lists_a_line_without_unit_and_a_missing_payment_condition()
    {
        // Review Focus 5: a unidade é anulável na entrada.
        var scenario = await PurchaseNfeTestSeed.SeedAsync();
        await ChangeAsync(scenario, i =>
        {
            i.Items.Single().UnitOfMeasureCode = null;
            i.PaymentConditionCode = null;
        });
        var reservation = new FakeNfeNumberReservationService();

        var e = await Assert.ThrowsAsync<DefaultException>(
            () => Issue(scenario, new FakeNfeSefazClient(), reservation).ExecuteAsync(scenario.InvoiceKey, "tester"));

        Assert.Contains("Item 1: unidade de medida", e.Message);
        Assert.Contains("Documento: condição de pagamento", e.Message);
        Assert.Equal(0, reservation.Calls);
    }

    [Fact]
    public async Task Readiness_names_the_supplier()
    {
        var scenario = await PurchaseNfeTestSeed.SeedAsync();
        var context = TestDb.CreateUnitOfWork(scenario.DatabaseName).Context;
        var address = await context.Set<Address>().SingleAsync(a => a.CardCode == PurchaseNfeTestSeed.Supplier);
        address.StreetNumber = null;
        await context.SaveChangesAsync();

        var e = await Assert.ThrowsAsync<DefaultException>(
            () => Issue(scenario, new FakeNfeSefazClient()).ExecuteAsync(scenario.InvoiceKey, "tester"));

        Assert.Contains($"Fornecedor {PurchaseNfeTestSeed.Supplier}: número do endereço de faturamento", e.Message);
    }

    [Fact]
    public async Task Entry_from_another_day_is_refused()
    {
        var scenario = await PurchaseNfeTestSeed.SeedAsync();
        await ChangeAsync(scenario, i => i.IssueDate = new DateTime(2026, 10, 1));

        var e = await Assert.ThrowsAsync<DefaultException>(
            () => Issue(scenario, new FakeNfeSefazClient()).ExecuteAsync(scenario.InvoiceKey, "tester"));

        Assert.StartsWith("A data do documento (01/10/2026) precisa ser a de hoje", e.Message);
    }

    [Fact]
    public async Task Processing_entry_is_consulted_and_confirmed()
    {
        var scenario = await PurchaseNfeTestSeed.SeedAsync();
        var sefaz = new FakeNfeSefazClient();
        sefaz.AuthorizeResponses.Enqueue(FakeNfeSefazClient.NoResponse);
        await Issue(scenario, sefaz).ExecuteAsync(scenario.InvoiceKey, "tester");
        sefaz.ConsultResponses.Enqueue(key => FakeNfeSefazClient.Authorized(key));

        var outcome = await new PurchaseInvoicesNfeConsultService(
                scenario.Db, new BranchNfeSettingsService(scenario.Db, new NfeOptions(NfeTestSeed.Config()), sefaz), sefaz,
                Handler(scenario), new FakeNfeNumberReservationService(), NullLogger<PurchaseInvoicesNfeConsultService>.Instance)
            .ExecuteAsync(scenario.InvoiceKey, "tester");

        Assert.Equal(NfeStatus.Authorized, outcome.NfeStatus);
        Assert.Equal(InvoiceStatus.Confirmed, (await ReloadAsync(scenario)).InvoiceStatus);
    }

    [Fact]
    public async Task Authorized_entry_xml_is_downloaded()
    {
        var scenario = await PurchaseNfeTestSeed.SeedAsync();
        var sefaz = new FakeNfeSefazClient();
        sefaz.AuthorizeResponses.Enqueue(key => FakeNfeSefazClient.Authorized(key));
        await Issue(scenario, sefaz).ExecuteAsync(scenario.InvoiceKey, "tester");

        var (bytes, fileName) = await new PurchaseInvoicesNfeXmlDownloadService(scenario.Db).ExecuteAsync(scenario.InvoiceKey);

        Assert.Equal($"{(await ReloadAsync(scenario)).ChaveNFe}-procNFe.xml", fileName);
        Assert.Contains("<nfeProc", System.Text.Encoding.UTF8.GetString(bytes));
    }

    [Fact]
    public async Task Confirmation_is_completed_for_an_authorized_pending_entry()
    {
        var scenario = await PurchaseNfeTestSeed.SeedAsync();
        await ChangeAsync(scenario, i => i.NfeStatus = NfeStatus.Authorized);

        var outcome = await new PurchaseInvoicesNfeCompleteConfirmationService(scenario.Db, Handler(scenario))
            .ExecuteAsync(scenario.InvoiceKey, "tester");

        Assert.Equal(InvoiceStatus.Confirmed, outcome.InvoiceStatus);
    }
}
