using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.SAP;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Entities.SAP;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Infra;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Tests.Sap;

/// <summary>
/// Regras da listagem "Vincular NF do SAP" (GAC-1226). SAP e Siagro são dois bancos: o InMemory
/// esconde LINQ intraduzível, por isso há um teste de <c>ToQueryString</c> no provider SqlServer.
/// </summary>
public class SapLinkableInvoicesServiceTests
{
    private const string Item = "SOJA";
    private const string Carrier = "F004702";
    private const string Customer = "C0001";
    private const string Plate = "ABC1D23";
    private const decimal Quantity = 90_000m;
    private static readonly DateTime Today = new(2026, 11, 16);

    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();
    private readonly SapErpDbContext _sap = new(
        new DbContextOptionsBuilder<SapErpDbContext>().UseInMemoryDatabase($"sap-{Guid.NewGuid()}").Options);

    private ShipmentLoad _load = null!;

    private sealed class FakeFiscal(bool available, params SapFiscalDocumentInfo[] infos) : ISapFiscalDocumentSource
    {
        public bool IsAvailable => available;

        public List<SapInvoiceRef> Requested { get; } = [];

        /// <summary>Finalidade por DocEntry; nulo = toda invoice pedida tem finalidade "1".</summary>
        public Dictionary<int, string>? Purposes { get; init; }

        public Task<IReadOnlyDictionary<int, SapFiscalDocumentInfo>> GetForInvoicesAsync(
            IReadOnlyCollection<SapInvoiceRef> invoices, CancellationToken ct = default)
        {
            Requested.AddRange(invoices);
            return Task.FromResult<IReadOnlyDictionary<int, SapFiscalDocumentInfo>>(
                infos.ToDictionary(i => i.DocEntry));
        }

        public Task<IReadOnlyDictionary<int, string>> GetInvoicePurposesAsync(
            IReadOnlyCollection<SapInvoiceRef> invoices, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyDictionary<int, string>>(
                Purposes ?? invoices.ToDictionary(i => i.DocEntry, _ => "1"));
    }

    private SapLinkableInvoicesService Service(ISapFiscalDocumentSource? fiscal = null) =>
        new(_db.Context, _sap, fiscal ?? new FakeFiscal(false),
            new ShipmentLoadsBillingGuardService(_db.Context));

    private async Task SeedLoadAsync(string? card = Customer, string? carrier = Carrier, string? plate = Plate)
    {
        _load = new ShipmentLoad
        {
            Key = Guid.NewGuid(),
            Code = "CG000010",
            BranchCode = "3",
            ItemCode = Item,
            UnitOfMeasureCode = "KG",
            CardCode = card,
            CarrierCardCode = carrier,
            TruckCode = plate,
            TotalQuantity = Quantity,
            Status = ShipmentLoadStatus.Open,
        };
        _db.Context.ShipmentLoads.Add(_load);
        await _db.SaveChangesAsync();
    }

    /// <summary>Invoice válida por padrão; cada teste estraga UMA regra.</summary>
    private async Task AddInvoiceAsync(int docEntry, Action<SapInvoice>? tweak = null,
        Action<SapInvoiceLine>? tweakLine = null, SapInvoiceTransport? transport = null,
        bool noTransport = false, int extraLines = 0)
    {
        var invoice = new SapInvoice
        {
            DocEntry = docEntry,
            DocNum = docEntry + 1000,
            DocDate = Today.AddDays(-1),
            CardCode = Customer,
            CardName = "CLIENTE TESTE",
            DocCur = "R$",
            BPLId = 3,
            Canceled = "N",
            Serial = 198105,
            SeriesStr = "1",
            Model = "39",
        };
        var line = new SapInvoiceLine
        {
            DocEntry = docEntry,
            LineNum = 0,
            ItemCode = Item,
            Quantity = Quantity,
            Price = 2.5m,
            LineTotal = Quantity * 2.5m,
            CfopCode = "5101",
            Usage = 70,
            UnitMsr = "KG",
        };
        tweak?.Invoke(invoice);
        tweakLine?.Invoke(line);

        _sap.SapInvoices.Add(invoice);
        _sap.SapInvoiceLines.Add(line);
        for (var i = 1; i <= extraLines; i++)
            _sap.SapInvoiceLines.Add(new SapInvoiceLine
                { DocEntry = docEntry, LineNum = i, ItemCode = Item, Quantity = 1m });

        if (!noTransport)
            _sap.SapInvoiceTransports.Add(transport ?? new SapInvoiceTransport
                { DocEntry = docEntry, Carrier = Carrier, Incoterms = "0", Vehicle = Plate });

        await _sap.SaveChangesAsync();
    }

    private Task<List<SapLinkableInvoiceDto>> ListAsync(SapLinkableInvoicesService? service = null) =>
        (service ?? Service()).ListAsync(_load.Key, Today.AddDays(-15), Today);

    [Fact]
    public async Task Valid_invoice_is_listed_with_formatted_number_series_and_fields()
    {
        await SeedLoadAsync();
        await AddInvoiceAsync(1);

        var dto = Assert.Single(await ListAsync());

        Assert.Equal(1, dto.DocEntry);
        Assert.Equal(1001, dto.DocNum);
        Assert.Equal("000198105", dto.TaxDocumentNumber);
        Assert.Equal("001", dto.TaxDocumentSeries);
        Assert.Equal("C0001", dto.CardCode);
        Assert.Equal(Quantity, dto.Quantity);
        Assert.Equal(2.5m, dto.UnitPrice);
        Assert.Equal("5101", dto.Cfop);
        Assert.Equal(70, dto.UsageCode);
        Assert.Null(dto.UsageName); // natureza fora do OUSG: fica só o código
        Assert.Equal(Carrier, dto.CarrierCode);
        Assert.Equal(FreightTerms.Cif, dto.FreightTerms);
        Assert.Null(dto.AccessKey);
    }

    [Fact]
    public async Task Usage_of_the_sap_line_comes_with_its_name()
    {
        await SeedLoadAsync();
        _sap.Usages.Add(new SiagroB1.Domain.Entities.SAP.Usage { Code = 70, Name = "S- Venda p/ Export." });
        await AddInvoiceAsync(1);
        await AddInvoiceAsync(2, tweakLine: l => l.Usage = null);

        var list = await ListAsync();

        var withUsage = list.Single(d => d.DocEntry == 1);
        Assert.Equal(70, withUsage.UsageCode);
        Assert.Equal("S- Venda p/ Export.", withUsage.UsageName);

        var withoutUsage = list.Single(d => d.DocEntry == 2);
        Assert.Null(withoutUsage.UsageCode);
        Assert.Null(withoutUsage.UsageName);
    }

    [Fact]
    public async Task Cancelled_invoice_is_left_out()
    {
        await SeedLoadAsync();
        await AddInvoiceAsync(1, i => i.Canceled = "Y");
        await AddInvoiceAsync(2, i => i.Canceled = "C");

        Assert.Empty(await ListAsync());
    }

    [Fact]
    public async Task Non_nfe_model_is_left_out()
    {
        await SeedLoadAsync();
        await AddInvoiceAsync(1, i => i.Model = "46");

        Assert.Empty(await ListAsync());
    }

    [Fact]
    public async Task Invoice_without_serial_is_left_out()
    {
        await SeedLoadAsync();
        await AddInvoiceAsync(1, i => i.Serial = null);

        Assert.Empty(await ListAsync());
    }

    [Fact]
    public async Task Invoice_with_two_lines_is_left_out()
    {
        await SeedLoadAsync();
        await AddInvoiceAsync(1, extraLines: 1);

        Assert.Empty(await ListAsync());
    }

    [Fact]
    public async Task Invoice_of_another_item_is_left_out()
    {
        await SeedLoadAsync();
        await AddInvoiceAsync(1, tweakLine: l => l.ItemCode = "MILHO");

        Assert.Empty(await ListAsync());
    }

    /// <summary>
    /// GAC-1226 rodada 2: a NF entra se a quantidade for maior que zero, inclusive acima do saldo — ver
    /// a NF ajuda a achar uma quantidade de carga digitada errada; quem barra é a regra da soma no vínculo.
    /// </summary>
    [Theory]
    [InlineData(0, true)]
    [InlineData(-50_000, true)]
    [InlineData(10_000, true)]
    [InlineData(-90_000, false)]
    [InlineData(-100_000, false)]
    public async Task Quantity_must_be_positive_with_no_upper_bound(double delta, bool listed)
    {
        await SeedLoadAsync();
        await AddInvoiceAsync(1, tweakLine: l => l.Quantity = Quantity + (decimal)delta);

        Assert.Equal(listed, (await ListAsync()).Count == 1);
    }

    [Fact]
    public async Task Available_balance_discounts_what_was_already_invoiced()
    {
        await SeedLoadAsync();
        // Nota viva de 30.000 na carga: o saldo cai para 60.000; as duas invoices aparecem (sem teto).
        var invoice = new SalesInvoice
        {
            Key = Guid.NewGuid(), CardCode = "C0001", BranchCode = "3", ShipmentLoadKey = _load.Key,
            InvoiceStatus = InvoiceStatus.Confirmed, InvoiceType = SalesInvoiceType.Normal,
        };
        invoice.AddItem(new SalesInvoiceItem
            { Key = Guid.NewGuid(), ItemCode = Item, UnitOfMeasureCode = "KG", Quantity = 30_000m });
        _db.Context.SalesInvoices.Add(invoice);
        await _db.SaveChangesAsync();
        await AddInvoiceAsync(1);
        await AddInvoiceAsync(2, tweakLine: l => l.Quantity = 60_000m);

        Assert.Equal([1, 2], (await ListAsync()).Select(d => d.DocEntry).OrderBy(d => d));
    }

    [Fact]
    public async Task Invoice_outside_the_period_is_left_out_and_the_end_date_includes_the_whole_day()
    {
        await SeedLoadAsync();
        await AddInvoiceAsync(1, i => i.DocDate = Today.AddDays(-16));
        await AddInvoiceAsync(2, i => i.DocDate = Today.AddHours(15)); // dentro do dia final
        await AddInvoiceAsync(3, i => i.DocDate = Today.AddDays(1));

        Assert.Equal(2, Assert.Single(await ListAsync()).DocEntry);
    }

    [Fact]
    public async Task Invoice_of_another_branch_is_left_out()
    {
        await SeedLoadAsync();
        await AddInvoiceAsync(1, i => i.BPLId = 6);
        await AddInvoiceAsync(2, i => i.BPLId = null);

        Assert.Empty(await ListAsync());
    }

    [Fact]
    public async Task Unauthorized_invoice_is_left_out_when_the_fiscal_addon_is_available()
    {
        await SeedLoadAsync();
        await AddInvoiceAsync(1); // autorizada
        await AddInvoiceAsync(2); // sem info nenhuma: não está autorizada
        await AddInvoiceAsync(3); // autorizada mas cancelada no add-on
        await AddInvoiceAsync(4); // não autorizada
        var fiscal = new FakeFiscal(true,
            new SapFiscalDocumentInfo(1, "KEY1", true, false, "135"),
            new SapFiscalDocumentInfo(3, "KEY3", true, true, "135"),
            new SapFiscalDocumentInfo(4, "KEY4", false, false, null));

        var dto = Assert.Single(await ListAsync(Service(fiscal)));

        Assert.Equal(1, dto.DocEntry);
        Assert.Equal("KEY1", dto.AccessKey);
        Assert.All(fiscal.Requested, r => Assert.Equal(3, r.BranchId));
    }

    [Fact]
    public async Task Unauthorized_invoice_is_listed_when_there_is_no_fiscal_addon()
    {
        await SeedLoadAsync();
        await AddInvoiceAsync(1);

        var dto = Assert.Single(await ListAsync(Service(new FakeFiscal(false))));

        Assert.Null(dto.AccessKey);
    }

    private async Task LinkAsync(int docEntry, InvoiceStatus status)
    {
        _db.Context.SalesInvoices.Add(new SalesInvoice
        {
            Key = Guid.NewGuid(), CardCode = "C0001", BranchCode = "3",
            InvoiceStatus = status, InvoiceType = SalesInvoiceType.Normal, SapDocEntry = docEntry,
        });
        await _db.SaveChangesAsync();
    }

    [Theory]
    [InlineData(InvoiceStatus.Confirmed, false)]
    [InlineData(InvoiceStatus.Pending, false)]
    [InlineData(InvoiceStatus.Cancelled, true)]
    [InlineData(InvoiceStatus.Returned, true)]
    public async Task Invoice_linked_to_an_active_document_is_left_out(InvoiceStatus status, bool listed)
    {
        await SeedLoadAsync();
        await AddInvoiceAsync(1);
        await LinkAsync(1, status);

        Assert.Equal(listed, (await ListAsync()).Count == 1);
    }

    [Fact]
    public async Task Linked_exclusion_works_in_every_chunk_of_ids()
    {
        await SeedLoadAsync();
        // 1.005 invoices, as mais novas primeiro: DocEntry 6..1005 formam o primeiro lote de 1.000
        // ids e 1..5 o segundo. Vincula TODO o primeiro lote e só o 3 do segundo: um laço que
        // processasse só o primeiro lote deixaria o 3 na lista.
        for (var i = 1; i <= 1_005; i++)
            await AddInvoiceAsync(i, inv => inv.DocDate = Today.AddDays(-1).AddMinutes(i));
        for (var i = 6; i <= 1_005; i++)
            _db.Context.SalesInvoices.Add(new SalesInvoice
            {
                Key = Guid.NewGuid(), CardCode = "C0001", BranchCode = "3",
                InvoiceStatus = InvoiceStatus.Confirmed, InvoiceType = SalesInvoiceType.Normal, SapDocEntry = i,
            });
        await _db.SaveChangesAsync();
        await LinkAsync(3, InvoiceStatus.Confirmed);

        var result = await ListAsync();

        Assert.Equal([5, 4, 2, 1], result.Select(r => r.DocEntry).ToArray());
    }

    [Theory]
    [InlineData("0", FreightTerms.Cif)]
    [InlineData("1", FreightTerms.Fob)]
    [InlineData("2", FreightTerms.Ter)]
    [InlineData("9", FreightTerms.None)]
    public async Task Incoterms_maps_to_the_freight_terms(string incoterms, FreightTerms expected)
    {
        await SeedLoadAsync();
        await AddInvoiceAsync(1, transport: new SapInvoiceTransport { DocEntry = 1, Carrier = Carrier, Incoterms = incoterms, Vehicle = Plate });

        Assert.Equal(expected, Assert.Single(await ListAsync()).FreightTerms);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("3")]
    [InlineData("CIF")]
    public async Task Empty_or_unmapped_incoterms_leave_the_invoice_out(string? incoterms)
    {
        await SeedLoadAsync();
        await AddInvoiceAsync(1, transport: new SapInvoiceTransport { DocEntry = 1, Carrier = Carrier, Incoterms = incoterms, Vehicle = Plate });

        Assert.Empty(await ListAsync());
        Assert.Null(await Service().FindAsync(_load.Key, 1));
    }

    [Fact]
    public async Task Invoice_without_transport_row_is_left_out()
    {
        await SeedLoadAsync();
        await AddInvoiceAsync(1, noTransport: true);

        Assert.Empty(await ListAsync());
    }

    [Fact]
    public async Task List_is_limited_to_200_rows_most_recent_first()
    {
        await SeedLoadAsync();
        for (var i = 1; i <= 205; i++)
            await AddInvoiceAsync(i, inv => inv.DocDate = Today.AddDays(-1).AddMinutes(i));

        var result = await ListAsync();

        Assert.Equal(200, result.Count);
        Assert.Equal(205, result[0].DocEntry);
    }

    [Fact]
    public async Task Find_applies_the_same_rules_without_the_period_filter()
    {
        await SeedLoadAsync();
        await AddInvoiceAsync(1, i => i.DocDate = Today.AddDays(-400));
        await AddInvoiceAsync(2, i => i.Canceled = "Y");
        await AddInvoiceAsync(3);
        await LinkAsync(3, InvoiceStatus.Confirmed);

        Assert.NotNull(await Service().FindAsync(_load.Key, 1));
        Assert.Null(await Service().FindAsync(_load.Key, 2));
        Assert.Null(await Service().FindAsync(_load.Key, 3));
        Assert.Null(await Service().FindAsync(_load.Key, 999));
    }

    [Fact]
    public async Task Unknown_load_is_a_business_error()
    {
        await Assert.ThrowsAsync<BusinessException>(() =>
            Service().ListAsync(Guid.NewGuid(), Today.AddDays(-15), Today));
    }

    private sealed class FakeDbException() : DbException("banco fora do ar");

    /// <summary>Costura de teste: faz a consulta ao SAP lançar o erro escolhido, sem rede.</summary>
    private sealed class FailingSapService(
        AppDbContext context, SapErpDbContext sap, ISapFiscalDocumentSource fiscal,
        ShipmentLoadsBillingGuardService guard, Exception failure)
        : SapLinkableInvoicesService(context, sap, fiscal, guard)
    {
        protected override Task<List<SapInvoiceProjection>> ExecuteSapQueryAsync(
            IQueryable<SapInvoiceProjection> query, CancellationToken ct) =>
            throw failure;
    }

    private sealed class FailingFiscal(Exception failure) : ISapFiscalDocumentSource
    {
        public bool IsAvailable => true;

        public Task<IReadOnlyDictionary<int, SapFiscalDocumentInfo>> GetForInvoicesAsync(
            IReadOnlyCollection<SapInvoiceRef> invoices, CancellationToken ct = default) =>
            throw failure;
    }

    /// <summary>Add-on que lê a situação mas cai na hora de ler a finalidade.</summary>
    private sealed class FailingPurposeFiscal(Exception failure) : ISapFiscalDocumentSource
    {
        public bool IsAvailable => true;

        public Task<IReadOnlyDictionary<int, SapFiscalDocumentInfo>> GetForInvoicesAsync(
            IReadOnlyCollection<SapInvoiceRef> invoices, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyDictionary<int, SapFiscalDocumentInfo>>(
                invoices.ToDictionary(i => i.DocEntry, i => new SapFiscalDocumentInfo(i.DocEntry, "K", true, false, "135")));

        public Task<IReadOnlyDictionary<int, string>> GetInvoicePurposesAsync(
            IReadOnlyCollection<SapInvoiceRef> invoices, CancellationToken ct = default) =>
            throw failure;
    }

    public static IEnumerable<object[]> Failures() =>
    [
        [new FakeDbException()],
        [new TimeoutException("tempo esgotado")],
    ];

    [Theory]
    [MemberData(nameof(Failures))]
    public async Task Sap_failure_becomes_a_business_error_in_list_and_find(Exception failure)
    {
        await SeedLoadAsync();
        var service = new FailingSapService(_db.Context, _sap, new FakeFiscal(false),
            new ShipmentLoadsBillingGuardService(_db.Context), failure);

        var list = await Assert.ThrowsAsync<BusinessException>(() => ListAsync(service));
        var find = await Assert.ThrowsAsync<BusinessException>(() => service.FindAsync(_load.Key, 1));

        const string message = "Não foi possível consultar as notas fiscais no SAP. Tente novamente em instantes.";
        Assert.Equal(message, list.Message);
        Assert.Equal(message, find.Message);
    }

    [Fact]
    public async Task Unexpected_sap_error_is_not_swallowed()
    {
        await SeedLoadAsync();
        var service = new FailingSapService(_db.Context, _sap, new FakeFiscal(false),
            new ShipmentLoadsBillingGuardService(_db.Context), new InvalidOperationException("bug"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => ListAsync(service));
    }

    [Theory]
    [MemberData(nameof(Failures))]
    public async Task Fiscal_addon_failure_becomes_a_business_error_in_list_and_find(Exception failure)
    {
        await SeedLoadAsync();
        await AddInvoiceAsync(1);
        var service = Service(new FailingFiscal(failure));

        var list = await Assert.ThrowsAsync<BusinessException>(() => ListAsync(service));
        var find = await Assert.ThrowsAsync<BusinessException>(() => service.FindAsync(_load.Key, 1));

        const string message = "Não foi possível consultar a situação das notas fiscais no add-on fiscal. Tente novamente em instantes.";
        Assert.Equal(message, list.Message);
        Assert.Equal(message, find.Message);
    }

    [Fact]
    public async Task Lists_only_invoices_of_the_load_customer()
    {
        await SeedLoadAsync();
        await AddInvoiceAsync(1);
        await AddInvoiceAsync(2, i => i.CardCode = "C0002");

        Assert.Equal(1, Assert.Single(await ListAsync()).DocEntry);
    }

    [Fact]
    public async Task Lists_only_invoices_of_the_load_carrier()
    {
        await SeedLoadAsync();
        await AddInvoiceAsync(1);
        await AddInvoiceAsync(2, transport: new SapInvoiceTransport
            { DocEntry = 2, Carrier = "OUTRA", Incoterms = "0", Vehicle = Plate });
        await AddInvoiceAsync(3, transport: new SapInvoiceTransport
            { DocEntry = 3, Carrier = "", Incoterms = "0", Vehicle = Plate });

        Assert.Equal(1, Assert.Single(await ListAsync()).DocEntry);
    }

    [Theory]
    [InlineData("abc-1d23")]
    [InlineData("ABC 1D23")]
    [InlineData("ABC1D23")]
    public async Task Plate_matches_ignoring_case_hyphen_and_space(string loadPlate)
    {
        await SeedLoadAsync(plate: loadPlate);
        await AddInvoiceAsync(1);                         // INV12.Vehicle = "ABC1D23"
        await AddInvoiceAsync(2, transport: new SapInvoiceTransport
            { DocEntry = 2, Carrier = Carrier, Incoterms = "0", Vehicle = "abc-1d23" });
        await AddInvoiceAsync(3, transport: new SapInvoiceTransport
            { DocEntry = 3, Carrier = Carrier, Incoterms = "0", Vehicle = "XYZ9K88" });
        await AddInvoiceAsync(4, transport: new SapInvoiceTransport
            { DocEntry = 4, Carrier = Carrier, Incoterms = "0", Vehicle = null });

        Assert.Equal([1, 2], (await ListAsync()).Select(d => d.DocEntry).OrderBy(d => d));
    }

    [Fact]
    public async Task Invoice_in_another_unit_or_currency_is_not_listed()
    {
        await SeedLoadAsync();
        await AddInvoiceAsync(1);
        await AddInvoiceAsync(2, tweakLine: l => l.UnitMsr = "TON");
        await AddInvoiceAsync(3, i => i.DocCur = "US$");

        Assert.Equal(1, Assert.Single(await ListAsync()).DocEntry);
    }

    [Fact]
    public async Task Complement_invoice_is_not_listed()
    {
        await SeedLoadAsync();
        await AddInvoiceAsync(1);
        await AddInvoiceAsync(2);
        var fiscal = new FakeFiscal(true,
            new SapFiscalDocumentInfo(1, "K1", true, false, "135"),
            new SapFiscalDocumentInfo(2, "K2", true, false, "135"))
        { Purposes = new() { [1] = "1", [2] = "2" } };

        Assert.Equal(1, Assert.Single(await ListAsync(Service(fiscal))).DocEntry);
    }

    [Fact]
    public async Task Invoice_without_readable_purpose_is_not_listed()
    {
        await SeedLoadAsync();
        await AddInvoiceAsync(1);
        await AddInvoiceAsync(2);
        var fiscal = new FakeFiscal(true,
            new SapFiscalDocumentInfo(1, "K1", true, false, "135"),
            new SapFiscalDocumentInfo(2, "K2", true, false, "135"))
        { Purposes = new() { [1] = "1" } };

        Assert.Equal(1, Assert.Single(await ListAsync(Service(fiscal))).DocEntry);
    }

    [Fact]
    public async Task Without_fiscal_addon_the_purpose_filter_does_not_apply()
    {
        await SeedLoadAsync();
        await AddInvoiceAsync(1);
        var fiscal = new FakeFiscal(false) { Purposes = [] };

        Assert.Single(await ListAsync(Service(fiscal)));
    }

    [Theory]
    [MemberData(nameof(Failures))]
    public async Task Fiscal_addon_failure_on_purposes_becomes_a_business_error(Exception failure)
    {
        await SeedLoadAsync();
        await AddInvoiceAsync(1);

        var ex = await Assert.ThrowsAsync<BusinessException>(() => ListAsync(Service(new FailingPurposeFiscal(failure))));

        Assert.Equal(
            "Não foi possível consultar a situação das notas fiscais no add-on fiscal. Tente novamente em instantes.",
            ex.Message);
    }

    [Theory]
    [InlineData(null, "T01", "ABC1D23", "Informe o cliente da carga para vincular a NF do SAP.")]
    [InlineData("C01", null, "ABC1D23", "Informe a transportadora da carga para vincular a NF do SAP.")]
    [InlineData("C01", "T01", " ", "Informe a placa da carga para vincular a NF do SAP.")]
    public async Task Load_missing_data_is_refused(string? card, string? carrier, string? plate, string message)
    {
        await SeedLoadAsync(card, carrier, plate);

        var list = await Assert.ThrowsAsync<BusinessException>(() => ListAsync());
        var find = await Assert.ThrowsAsync<BusinessException>(() => Service().FindAsync(_load.Key, 1));

        Assert.Equal(message, list.Message);
        Assert.Equal(message, find.Message);
    }

    [Theory]
    [InlineData("abc-1d23", "ABC1D23")]
    [InlineData(" ab c-1 ", "ABC1")]
    [InlineData(null, "")]
    [InlineData("  ", "")]
    public void NormalizePlate_uppercases_and_drops_hyphen_and_space(string? plate, string expected) =>
        Assert.Equal(expected, SapLinkableInvoicesService.NormalizePlate(plate));

    [Fact]
    public void Sap_queries_translate_to_sql_server()
    {
        // Provider SqlServer com connection string fictícia: ToQueryString não abre conexão, mas
        // estoura em LINQ que o InMemory aceitaria e o SQL Server não.
        using var sap = new SapErpDbContext(new DbContextOptionsBuilder<SapErpDbContext>()
            .UseSqlServer("Server=localhost;Database=dummy;User Id=x;Password=x;TrustServerCertificate=True").Options);

        var list = SapLinkableInvoicesService.BuildSapQuery(
            sap, Item, 3, "C0001", "F004702", "ABC1D23", Today.AddDays(-15), Today.AddDays(1), null).ToQueryString();
        var find = SapLinkableInvoicesService.BuildSapQuery(
            sap, Item, 3, "C0001", "F004702", "ABC1D23", null, null, 42).ToQueryString();

        foreach (var sql in new[] { list, find })
        {
            Assert.Contains("OINV", sql);
            Assert.Contains("INV1", sql);
            Assert.Contains("INV12", sql);
            Assert.Contains("CANCELED", sql);
            Assert.Contains("Model", sql);
            Assert.Contains("Serial", sql);
            Assert.Contains("BPLId", sql);
            Assert.Contains("Incoterms", sql);
            Assert.Contains("[Vehicle]", sql);
            Assert.Contains("[DocCur]", sql);
            Assert.Contains("[unitMsr]", sql);
            Assert.Contains("REPLACE", sql.ToUpperInvariant());
            Assert.Contains("UPPER", sql.ToUpperInvariant());
            Assert.Contains("COUNT", sql.ToUpperInvariant());
        }

        // Período e DocEntry viajam como parâmetro, nunca concatenados.
        Assert.Contains("DocDate", list);
        Assert.Contains("@", list);
        Assert.Contains("DocEntry] = @", find);
    }
}
