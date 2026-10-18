using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services;
using SiagroB1.Application.Services.Nfe;
using SiagroB1.Application.Services.SalesContracts;
using SiagroB1.Application.Services.SalesInvoices;
using SiagroB1.Application.Services.SalesShipmentReleases;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Application.Services.Taxes;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Fiscal.Nfe;
using SiagroB1.Fiscal.Payments;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.SalesInvoices;

/// <summary>
/// Spec 2026-10-18 S7 e S9: o complemento de ICMS de saída é emitido Pendente como NF-e complementar (finNFe=2), sem
/// pagamento nem transporte, referenciando a venda original; a original não é cancelada (documento nem NF-e) enquanto
/// houver complemento não cancelado apontando para ela.
/// </summary>
public class SalesInvoiceIcmsComplementIssueTests
{
    private static readonly XNamespace Nfe = "http://www.portalfiscal.inf.br/nfe";
    private const string V3 = "Item SOJA: informe a base e o valor do ICMS do complemento.";
    private const string V4 = "Item SOJA: o ICMS do complemento não pode ser maior que a base.";
    private const string V5 = "A nota original 000002388 não está mais autorizada: o complemento não pode ser emitido.";
    private const string Justification = "Venda lancada em duplicidade";

    private static string V6(string? complementNumber) =>
        $"A nota 000002388 tem o complemento de ICMS {complementNumber} ativo: cancele o complemento antes.";

    private sealed record Scenario(
        UnitOfWork Db, string DatabaseName, Guid SaleKey, Guid SojaKey, FakeDocNumberSequenceService Numbers);

    /// <summary>
    /// A venda autorizada do <see cref="NfeReturnTestSeed"/>, com a natureza tributada (o motor roda com valor 0) e
    /// redução de alíquota no IBS/CBS (o grupo gRed tem de sair mesmo zerado).
    /// </summary>
    private static async Task<Scenario> SeedAsync()
    {
        var s = await NfeReturnTestSeed.SeedAsync();
        var db = s.Sale.Db;
        var usage = await db.Context.Usages.SingleAsync(u => u.Name == "Venda de grãos");
        usage.IcmsOutStateCst = "00";
        usage.PisCst = "01";
        usage.CofinsCst = "01";
        usage.IbsCbsCst = "200";
        usage.IbsCbsClassCode = "200036";
        usage.IbsRateReduction = 60m;
        usage.CbsRateReduction = 60m;
        var sale = await db.Context.SalesInvoices.SingleAsync(i => i.Key == s.Sale.InvoiceKey);
        sale.NfeProtocol = NfeCancelTestServices.AuthorizationProtocol;
        sale.NfeEnvironment = NfeEnvironment.Homologation;
        await db.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();

        return new Scenario(db, s.Sale.DatabaseName, s.Sale.InvoiceKey, s.SaleItemKey, new FakeDocNumberSequenceService());
    }

    private static SalesInvoicesCreateIcmsComplementService Create(Scenario s)
    {
        var partners = new FakeBusinessPartnerService(
            names: new() { [NfeTestSeed.CardCode] = "CLIENTE BA LTDA" },
            states: new() { [NfeTestSeed.CardCode] = "BA" });

        return new SalesInvoicesCreateIcmsComplementService(
            s.Db, TaxTestServices.Gate(s.Db, "STANDALONE"), TaxTestServices.Apply(s.Db, partners, "STANDALONE"), s.Numbers,
            NullLogger<SalesInvoicesCreateIcmsComplementService>.Instance, NfeTestSeed.Clock, NfeIssueInputAssembler.BrasiliaZone);
    }

    private static async Task<SalesInvoice> ComplementAsync(Scenario s, decimal icmsBase = 1_000m, decimal icmsValue = 120m)
    {
        var created = await Create(s).ExecuteAsync(s.SaleKey, [s.SojaKey], [icmsBase], [icmsValue], "ana");
        s.Db.Context.ChangeTracker.Clear();
        return created;
    }

    /// <summary>A confirmação REAL: o resultado autorizado confirma o complemento por ela.</summary>
    private static SalesInvoicesConfirmService Confirm(UnitOfWork db)
    {
        var usages = new UsageService(db, NullLogger<UsageService>.Instance);

        return new SalesInvoicesConfirmService(db,
            new SalesShipmentReleasesRecalculateShippedService(db.Context),
            new SalesContractsAllocationCreateService(db, new SalesContractsFixedVolumeService(db.Context)),
            new SalesContractsAllocationCreateForReturnService(db, new SalesContractsFixedVolumeService(db.Context)),
            new SalesInvoicesUsageGuardService(usages),
            new SalesContractsAllocationCreateForFiscalAdjustmentService(db, new SalesContractsFixedVolumeService(db.Context)),
            new ShipmentLoadsBalanceHookService(db.Context, new ShipmentLoadsMovementLogService(db.Context)),
            new ShipmentLoadsClosureHookService(db.Context, new ShipmentLoadsChangeLogService(db.Context)),
            new FakeStringLocalizer<Resource>(),
            TaxTestServices.Gate(db, "STANDALONE"));
    }

    private static SalesInvoicesNfeIssueService Issue(
        UnitOfWork db, FakeNfeSefazClient sefaz, FakeNfeNumberReservationService? reservation = null)
    {
        var config = NfeTestSeed.Config();
        var options = new NfeOptions(config);

        return new SalesInvoicesNfeIssueService(
            db, new TaxCalculationGate(db, config), new NfeReadinessValidator(db, options),
            new BranchNfeSettingsService(db, options, sefaz), reservation ?? new FakeNfeNumberReservationService(), sefaz,
            new SalesInvoiceNfeResultHandler(db, Confirm(db), NullLogger<SalesInvoiceNfeResultHandler>.Instance),
            options, NullLogger<SalesInvoicesNfeIssueService>.Instance, NfeTestSeed.Clock, NfeIssueInputAssembler.BrasiliaZone);
    }

    private static NfeReadinessValidator Readiness(UnitOfWork db) => new(db, new NfeOptions(NfeTestSeed.Config()));

    private static Task<SalesInvoice> LoadAsync(UnitOfWork db, Guid? key) =>
        db.Context.SalesInvoices.Include(i => i.Items).SingleAsync(i => i.Key == key);

    private static async Task ChangeAsync(UnitOfWork db, Guid? key, Action<SalesInvoice> change)
    {
        var invoice = await LoadAsync(db, key);
        change(invoice);
        await db.SaveChangesAsync();
        db.Context.ChangeTracker.Clear();
    }

    [Fact]
    public async Task Complement_is_issuable_while_pending_without_payment_or_weights()
    {
        var s = await SeedAsync();
        var complement = await ComplementAsync(s);
        // Transporte gravado no cabeçalho por fora (PATCH): o complemento não transporta nada.
        await ChangeAsync(s.Db, complement.Key, c =>
        {
            c.TruckingCompanyCode = "T-001";
            c.TruckCode = "ABC-1D23";
            c.FreightTerms = FreightTerms.Cif;
            c.GrossWeight = 10m;
            c.NetWeight = 10m;
            c.VolumeQuantity = 3;
        });
        var invoice = await LoadAsync(s.Db, complement.Key);
        Assert.Equal(InvoiceStatus.Pending, invoice.InvoiceStatus);
        Assert.Null(invoice.PaymentConditionCode);

        var context = await Readiness(s.Db).ValidateAsync(invoice);
        invoice.TaxDocumentNumber = "000000010";
        invoice.TaxDocumentSeries = "1";
        invoice.NfeRandomCode = "12345678";
        var input = NfeIssueInputAssembler.Build(invoice, context, NfeTestSeed.Clock(), null);

        Assert.Null(context.PaymentCondition);
        Assert.Equal(NfeDirection.Outgoing, input.Direction);
        Assert.Equal(NfePurpose.Complement, input.Purpose);
        Assert.Equal([NfeReturnTestSeed.SaleAccessKey], input.ReferencedKeys);
        Assert.Equal(PaymentMeansCodes.NoPayment, input.Payment.PaymentMeans);
        Assert.Equal(0m, input.Payment.PaidAmount);
        Assert.Equal(FreightTerms.None, input.FreightTerms);
        Assert.Null(input.Carrier);
        Assert.Null(input.Vehicle);
        Assert.Equal((0m, 0m), (input.GrossWeight, input.NetWeight));
        Assert.Null(input.Volume);
    }

    [Fact]
    public async Task Complement_is_issued_as_a_complementary_nfe_and_confirmed()
    {
        var s = await SeedAsync();
        var complement = await ComplementAsync(s);
        var sefaz = new FakeNfeSefazClient();
        sefaz.AuthorizeResponses.Enqueue(key => FakeNfeSefazClient.Authorized(key));

        var outcome = await Issue(s.Db, sefaz).ExecuteAsync(complement.Key, "tester");

        Assert.Equal(NfeStatus.Authorized, outcome.NfeStatus);
        var xml = XDocument.Parse(Assert.Single(sefaz.Sent).Xml);
        var ide = xml.Descendants(Nfe + "ide").Single();
        Assert.Equal("1", ide.Element(Nfe + "tpNF")!.Value);
        Assert.Equal("2", ide.Element(Nfe + "finNFe")!.Value);
        Assert.Equal(NfeReturnTestSeed.SaleAccessKey, ide.Elements(Nfe + "NFref").Single().Element(Nfe + "refNFe")!.Value);
        Assert.Equal("90", xml.Descendants(Nfe + "detPag").Single().Element(Nfe + "tPag")!.Value);
        Assert.Empty(xml.Descendants(Nfe + "cobr"));
        Assert.Equal("9", xml.Descendants(Nfe + "modFrete").Single().Value);
        Assert.Empty(xml.Descendants(Nfe + "vol"));
        Assert.Empty(xml.Descendants(Nfe + "transporta"));
        var prod = xml.Descendants(Nfe + "prod").Single();
        Assert.Equal("0.00", prod.Element(Nfe + "vProd")!.Value);
        Assert.Equal("6102", prod.Element(Nfe + "CFOP")!.Value);
        var icms = xml.Descendants(Nfe + "ICMS00").Single();
        Assert.Equal(("1000.00", "120.00"), (icms.Element(Nfe + "vBC")!.Value, icms.Element(Nfe + "vICMS")!.Value));
        var total = xml.Descendants(Nfe + "ICMSTot").Single();
        Assert.Equal("1000.00", total.Element(Nfe + "vBC")!.Value);
        Assert.Equal("120.00", total.Element(Nfe + "vICMS")!.Value);
        Assert.Equal("0.00", total.Element(Nfe + "vProd")!.Value);
        Assert.Equal("0.00", total.Element(Nfe + "vNF")!.Value);
        // IBS/CBS: alíquotas da natureza, valores 0, e o gRed (redução de alíquota) presente.
        var ibsCbs = xml.Descendants(Nfe + "gIBSCBS").Single();
        Assert.Equal("0.00", ibsCbs.Element(Nfe + "vBC")!.Value);
        var cbs = ibsCbs.Element(Nfe + "gCBS")!;
        Assert.Equal("0.9000", cbs.Element(Nfe + "pCBS")!.Value);
        Assert.Equal("0.00", cbs.Element(Nfe + "vCBS")!.Value);
        Assert.NotNull(cbs.Element(Nfe + "gRed"));
        Assert.NotNull(ibsCbs.Element(Nfe + "gIBSUF")!.Element(Nfe + "gRed"));

        // O resultado autorizado confirma o complemento (a Task 5 tira dessa confirmação os efeitos de venda).
        var saved = await TestDb.CreateUnitOfWork(s.DatabaseName).Context.SalesInvoices
            .AsNoTracking().SingleAsync(i => i.Key == complement.Key);
        Assert.Null(saved.NfeConfirmationError);
        Assert.Equal(InvoiceStatus.Confirmed, saved.InvoiceStatus);
        Assert.Equal(NfeStatus.Authorized, saved.NfeStatus);
    }

    [Fact]
    public async Task Readiness_refuses_line_without_icms()
    {
        var s = await SeedAsync();
        var complement = await ComplementAsync(s);
        // Gravado fora do serviço: a criação e o PATCH de linha recusariam (V3).
        await ChangeAsync(s.Db, complement.Key, c => c.Items.Single().IcmsBase = 0m);
        var invoice = await LoadAsync(s.Db, complement.Key);

        var e = await Assert.ThrowsAsync<DefaultException>(() => Readiness(s.Db).ValidateAsync(invoice));

        Assert.Equal(V3, e.Message);
    }

    [Fact]
    public async Task Readiness_refuses_icms_above_base()
    {
        var s = await SeedAsync();
        var complement = await ComplementAsync(s);
        await ChangeAsync(s.Db, complement.Key, c => c.Items.Single().IcmsValue = 1_500m);
        var invoice = await LoadAsync(s.Db, complement.Key);

        var e = await Assert.ThrowsAsync<DefaultException>(() => Readiness(s.Db).ValidateAsync(invoice));

        Assert.Equal(V4, e.Message);
    }

    [Fact]
    public async Task Line_without_icms_is_refused_before_reserving_a_number()
    {
        var s = await SeedAsync();
        var complement = await ComplementAsync(s);
        await ChangeAsync(s.Db, complement.Key, c => c.Items.Single().IcmsValue = 0m);
        var sefaz = new FakeNfeSefazClient();
        var reservation = new FakeNfeNumberReservationService();

        var e = await Assert.ThrowsAsync<DefaultException>(() =>
            Issue(s.Db, sefaz, reservation).ExecuteAsync(complement.Key, "tester"));

        Assert.Equal(V3, e.Message);
        Assert.Empty(sefaz.Sent);
        Assert.Equal(0, reservation.Calls);
    }

    [Theory]
    [InlineData(InvoiceStatus.Cancelled, NfeStatus.Cancelled)]
    [InlineData(InvoiceStatus.Confirmed, NfeStatus.Cancelled)]
    public async Task Original_cancelled_before_issue_is_refused(InvoiceStatus status, NfeStatus nfeStatus)
    {
        var s = await SeedAsync();
        var complement = await ComplementAsync(s);
        await ChangeAsync(s.Db, s.SaleKey, o =>
        {
            o.InvoiceStatus = status;
            o.NfeStatus = nfeStatus;
        });
        var sefaz = new FakeNfeSefazClient();
        var reservation = new FakeNfeNumberReservationService();

        var e = await Assert.ThrowsAsync<DefaultException>(() =>
            Issue(s.Db, sefaz, reservation).ExecuteAsync(complement.Key, "tester"));

        Assert.Equal(V5, e.Message);
        Assert.Empty(sefaz.Sent);
        Assert.Equal(0, reservation.Calls);
        Assert.Null((await s.Db.Context.SalesInvoices.AsNoTracking().SingleAsync(i => i.Key == complement.Key)).TaxDocumentNumber);
    }

    [Fact]
    public async Task Cancelling_original_with_active_complement_is_refused()
    {
        var s = await SeedAsync();
        var complement = await ComplementAsync(s);
        var sefaz = new FakeNfeSefazClient();

        var nfeCancel = await Assert.ThrowsAsync<DefaultException>(() =>
            NfeCancelTestServices.SalesCancel(s.Db, sefaz).ExecuteAsync(s.SaleKey, Justification, "tester"));
        var documentCancel = await Assert.ThrowsAsync<DefaultException>(() =>
            SalesInvoicesCancelServiceTests.Service(s.Db).EnsureCanCancelAsync(s.SaleKey));

        Assert.Equal(V6(complement.InvoiceNumber), nfeCancel.Message);
        Assert.Equal(V6(complement.InvoiceNumber), documentCancel.Message);
        Assert.Empty(sefaz.CancelRequests);
        var sale = await s.Db.Context.SalesInvoices.AsNoTracking().SingleAsync(i => i.Key == s.SaleKey);
        Assert.Equal((InvoiceStatus.Confirmed, NfeStatus.Authorized), (sale.InvoiceStatus, sale.NfeStatus));
    }

    [Fact]
    public async Task Completing_the_original_cancellation_after_sefaz_is_refused_with_active_complement()
    {
        var s = await SeedAsync();
        var complement = await ComplementAsync(s);
        await ChangeAsync(s.Db, s.SaleKey, o => o.NfeStatus = NfeStatus.Cancelled);

        var e = await Assert.ThrowsAsync<DefaultException>(() =>
            SalesInvoicesCancelServiceTests.Service(s.Db).CancelAfterNfeAsync(s.SaleKey, "tester"));

        Assert.Equal(V6(complement.InvoiceNumber), e.Message);
    }

    [Fact]
    public async Task Two_complements_block_the_original_until_both_are_cancelled()
    {
        var s = await SeedAsync();
        var first = await ComplementAsync(s);
        var second = await ComplementAsync(s, 500m, 30m);
        Assert.NotEqual(first.InvoiceNumber, second.InvoiceNumber);

        await ChangeAsync(s.Db, first.Key, c => c.InvoiceStatus = InvoiceStatus.Cancelled);

        var e = await Assert.ThrowsAsync<DefaultException>(() =>
            SalesInvoicesCancelServiceTests.Service(s.Db).EnsureCanCancelAsync(s.SaleKey));
        Assert.Equal(V6(second.InvoiceNumber), e.Message);

        await ChangeAsync(s.Db, second.Key, c => c.InvoiceStatus = InvoiceStatus.Cancelled);
        await SalesInvoicesCancelServiceTests.Service(s.Db).EnsureCanCancelAsync(s.SaleKey);
    }

    [Fact]
    public async Task Cancelled_complement_does_not_block()
    {
        var s = await SeedAsync();
        var complement = await ComplementAsync(s);
        await ChangeAsync(s.Db, complement.Key, c => c.InvoiceStatus = InvoiceStatus.Cancelled);
        var sefaz = new FakeNfeSefazClient();
        sefaz.CancelResponses.Enqueue(r => FakeNfeSefazClient.CancellationRegistered(r.AccessKey));

        await SalesInvoicesCancelServiceTests.Service(s.Db).EnsureCanCancelAsync(s.SaleKey);
        await NfeCancelTestServices.SalesCancel(s.Db, sefaz).ExecuteAsync(s.SaleKey, Justification, "tester");

        Assert.Single(sefaz.CancelRequests);
    }
}
