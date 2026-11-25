using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services;
using SiagroB1.Application.Services.SalesContracts;
using SiagroB1.Application.Services.SalesInvoices;
using SiagroB1.Application.Services.SalesShipmentReleases;
using SiagroB1.Application.Services.SAP;
using SiagroB1.Application.Services.ShipmentBilling;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Application.Tests.Accounting;
using SiagroB1.Application.Tests.SalesContracts;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Entities.SAP;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Infra;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Tests.ShipmentLoads;

/// <summary>
/// Ação "Vincular NF do SAP" (GAC-1226): relê e revalida tudo no servidor e entrega ao orquestrador do
/// faturamento da carga, para que os efeitos sejam os mesmos do faturamento manual.
/// </summary>
public class ShipmentLoadsLinkSapInvoiceServiceTests
{
    private const string CardCode = "C0001";
    private const string Carrier = "F004702";
    private const string Item = "SOJA";
    private const decimal Quantity = 90_000m;
    private const string SapBranch = "3";

    private readonly string _dbName = Guid.NewGuid().ToString();
    private readonly UnitOfWork _db;
    private readonly SapErpDbContext _sap = new(
        new DbContextOptionsBuilder<SapErpDbContext>().UseInMemoryDatabase($"sap-{Guid.NewGuid()}").Options);

    private ShipmentLoad _load = null!;
    private SalesContract _contract = null!;
    private SalesShipmentRelease _release = null!;

    public ShipmentLoadsLinkSapInvoiceServiceTests() => _db = TestDb.CreateUnitOfWork(_dbName);

    private ShipmentLoadsLinkSapInvoiceServiceTests(IInterceptor interceptor, string dbName)
    {
        _dbName = dbName;
        _db = TestDb.CreateUnitOfWork(dbName, interceptor);
    }

    private sealed class FakeFiscal(params SapFiscalDocumentInfo[] infos) : ISapFiscalDocumentSource
    {
        public bool IsAvailable => infos.Length > 0;

        public Task<IReadOnlyDictionary<int, SapFiscalDocumentInfo>> GetForInvoicesAsync(
            IReadOnlyCollection<SapInvoiceRef> invoices, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyDictionary<int, SapFiscalDocumentInfo>>(infos.ToDictionary(i => i.DocEntry));

        public Task<IReadOnlyDictionary<int, string>> GetInvoicePurposesAsync(
            IReadOnlyCollection<SapInvoiceRef> invoices, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyDictionary<int, string>>(invoices.ToDictionary(i => i.DocEntry, _ => "1"));
    }

    /// <summary>
    /// Registra Begin/Commit/Rollback: o InMemory não desfaz nada no rollback, então a atomicidade do
    /// lote é provada pelo protocolo (uma transação, commit só no fim, rollback na falha).
    /// </summary>
    private sealed class RecordingUnitOfWork(UnitOfWork inner) : IUnitOfWork
    {
        public List<string> Log { get; } = [];
        public AppDbContext Context => inner.Context;
        public Task BeginTransactionAsync() { Log.Add("Begin"); return inner.BeginTransactionAsync(); }
        public Task CommitAsync() { Log.Add("Commit"); return inner.CommitAsync(); }
        public Task RollbackAsync() { Log.Add("Rollback"); return inner.RollbackAsync(); }
        public Task SaveChangesAsync() => inner.SaveChangesAsync();
    }

    private RecordingUnitOfWork? _recorder;

    private ShipmentLoadsLinkSapInvoiceService Service(
        string erp = "SAPB1", ISapFiscalDocumentSource? fiscal = null)
    {
        _recorder = new RecordingUnitOfWork(_db);
        var usages = new Services.UsageService(_db, NullLogger<Services.UsageService>.Instance);
        IBusinessPartnerService partners = new FakeBusinessPartnerService(
            names: new Dictionary<string, string> { [CardCode] = "CLIENTE TESTE" },
            states: new Dictionary<string, string> { [CardCode] = "RS" });
        var config = AccountingTestData.Config(erp);

        var billing = new ShipmentBillingCreateSalesInvoiceService(
            _recorder,
            new SalesInvoicesCreateService(
                _db,
                partners,
                new FakeItemService(new Dictionary<string, string> { [Item] = "SOJA EM GRAOS" }),
                new FakeDocNumberSequenceService(),
                new SalesInvoicesUsageGuardService(usages),
                new SalesInvoicesCfopResolveService(_db, usages, partners),
                TaxTestServices.InactiveApply(_db),
                TaxTestServices.FiscalComplement(_db, "SAPB1"), NullLogger<SalesInvoicesCreateService>.Instance),
            new ShipmentBillingTransactionGuardService(_db.Context),
            new SalesShipmentReleaseMovementGuardService(_db.Context),
            new SalesShipmentReleasesRecalculateShippedService(_db.Context),
            new SalesContractsAllocationCreateService(
                _db, new SalesContractsFixedVolumeService(_db.Context)),
            new ShipmentLoadsBillingGuardService(_db.Context),
            new ShipmentLoadsRecalculateInvoicedService(_db, new ShipmentLoadsChangeLogService(_db.Context)),
            new ShipmentLoadsMovementLogService(_db.Context),
            NullLogger<ShipmentBillingCreateSalesInvoiceService>.Instance,
            configuration: config);

        return new ShipmentLoadsLinkSapInvoiceService(
            _recorder,
            new SapLinkableInvoicesService(
                _db.Context, _sap, fiscal ?? new FakeFiscal(), new ShipmentLoadsBillingGuardService(_db.Context)),
            new SalesInvoiceTaxDocumentDuplicateGuard(_db.Context),
            billing,
            new SalesContractsFixedVolumeService(_db.Context),
            config);
    }

    /// <remarks>
    /// O contrato nasce a R$ 2,50/kg, o mesmo preço das NFs de <see cref="AddSapInvoiceAsync"/>: assim o vínculo
    /// com a quantidade igual ao saldo é Normal (sem divergência de valor).
    /// </remarks>
    private async Task SeedAsync(string? releaseCard = null, ReleaseStatus releaseStatus = ReleaseStatus.Actived,
        string contractItem = Item, decimal? freightPrice = 12.5m, decimal total = Quantity,
        decimal contractPrice = 2.5m, CurrencyType? currency = null)
    {
        _load = new ShipmentLoad
        {
            Key = Guid.NewGuid(),
            Code = "CG000010",
            BranchCode = SapBranch,
            ItemCode = Item,
            ItemName = "SOJA EM GRAOS",
            UnitOfMeasureCode = "KG",
            CardCode = CardCode,
            TruckCode = "ABC1D23",
            TruckDriverCode = "MOT01",
            CarrierCardCode = Carrier,
            CarrierName = "TRANSPORTES YOKOTOBI LTDA",
            TotalQuantity = total,
            FreightPrice = freightPrice,
            Status = ShipmentLoadStatus.Open,
        };

        _contract = SalesContractsAllocationTestSupport.NewContract(
            totalVolume: 1_000_000m, price: contractPrice, cardCode: releaseCard ?? CardCode, itemCode: contractItem);
        _contract.Type = ContractType.Fixed;
        _contract.StandardCurrency = currency ?? CurrencyType.Brl;
        _release = SalesContractsAllocationTestSupport.NewRelease(
            _contract.Key, released: 1_000_000m, status: releaseStatus);
        _release.DeliveryLocationCode = "LOCAL77";

        _db.Context.ShipmentLoads.Add(_load);
        _db.Context.SalesContracts.Add(_contract);
        _db.Context.SalesShipmentReleases.Add(_release);
        _db.Context.Branchs.Add(new Branch { Code = SapBranch, BranchName = "FILIAL 3", StateCode = "RS" });
        await _db.SaveChangesAsync();
    }

    private async Task AddSapInvoiceAsync(int docEntry = 1, decimal quantity = Quantity,
        string carrier = Carrier, string incoterms = "1", string? canceled = "N", string cardCode = CardCode,
        int? serial = null, decimal price = 2.5m, decimal? lineTotal = null)
    {
        _sap.SapInvoices.Add(new SapInvoice
        {
            DocEntry = docEntry,
            DocNum = docEntry + 1000,
            DocDate = new DateTime(2026, 11, 15),
            CardCode = cardCode,
            CardName = "CLIENTE TESTE",
            DocCur = "R$",
            BPLId = 3,
            Canceled = canceled,
            Serial = serial ?? 198104 + docEntry,
            SeriesStr = "1",
            Model = "39",
        });
        _sap.SapInvoiceLines.Add(new SapInvoiceLine
        {
            DocEntry = docEntry, LineNum = 0, ItemCode = Item, Quantity = quantity,
            Price = price, LineTotal = lineTotal ?? quantity * price, CfopCode = "5101", Usage = 70, UnitMsr = "KG",
        });
        if (!await _sap.Usages.AnyAsync(u => u.Code == 70))
            _sap.Usages.Add(new SiagroB1.Domain.Entities.SAP.Usage { Code = 70, Name = "S- Venda p/ Export." });
        _sap.SapInvoiceTransports.Add(new SapInvoiceTransport
            { DocEntry = docEntry, Carrier = carrier, Incoterms = incoterms, Vehicle = "ABC1D23" });
        await _sap.SaveChangesAsync();
    }

    private async Task<int> InvoiceCountAsync() => await _db.Context.SalesInvoices.AsNoTracking().CountAsync();

    private async Task AssertNothingPersistedAsync()
    {
        Assert.Equal(0, await InvoiceCountAsync());
        var load = await _db.Context.ShipmentLoads.AsNoTracking().SingleAsync();
        Assert.Equal(ShipmentLoadStatus.Open, load.Status);
        Assert.Equal(0m, load.InvoicedQuantity);
    }

    private Task<IReadOnlyList<SalesInvoice>> LinkAsync(
        ShipmentLoadsLinkSapInvoiceService? service = null, params int[] docEntries) =>
        (service ?? Service()).ExecuteAsync(
            _load.Key, docEntries.Length == 0 ? [1] : docEntries, _release.Key, "tester");

    private Task<IReadOnlyList<SalesInvoice>> ConfirmedLinkAsync(params int[] docEntries) =>
        Service().ExecuteAsync(
            _load.Key, docEntries.Length == 0 ? [1] : docEntries, _release.Key, "tester", confirmDivergence: true);

    [Fact]
    public async Task Links_creating_a_confirmed_document_mirroring_the_sap_invoice()
    {
        await SeedAsync();
        await AddSapInvoiceAsync();

        var created = await LinkAsync();

        var saved = await _db.Context.SalesInvoices.AsNoTracking().Include(i => i.Items).SingleAsync();
        Assert.Equal(Assert.Single(created).Key, saved.Key);
        Assert.Equal(InvoiceStatus.Confirmed, saved.InvoiceStatus);
        Assert.Equal(TaxDocumentKind.Nfe, saved.TaxDocumentKind);
        Assert.False(saved.WithoutTaxDocument);
        Assert.Equal(NfeStatus.None, saved.NfeStatus);
        Assert.Equal(1, saved.SapDocEntry);
        Assert.Equal(1001, saved.SapDocNum);
        Assert.Equal("000198105", saved.TaxDocumentNumber);
        Assert.Equal("001", saved.TaxDocumentSeries);
        Assert.Equal(new DateTime(2026, 11, 15), saved.InvoiceDate!.Value.Date);
        Assert.Equal(CardCode, saved.CardCode);
        Assert.Equal(FreightTerms.Fob, saved.FreightTerms);
        Assert.Equal(_load.Key, saved.ShipmentLoadKey);
        Assert.Equal(SapBranch, saved.BranchCode);
        Assert.Equal(Quantity, saved.GrossWeight);
        Assert.Equal(Quantity, saved.NetWeight);
        Assert.Equal("ABC1D23", saved.TruckCode);

        var line = Assert.Single(saved.Items);
        Assert.Equal(Item, line.ItemCode);
        Assert.Equal(Quantity, line.Quantity);
        Assert.Equal(2.5m, line.UnitPrice);
        Assert.Equal("5101", line.Cfop);
        // Natureza da linha da invoice do SAP — sem efeito configurado no Siagro, e mesmo assim gravada.
        Assert.Equal(70, line.UsageCode);
        Assert.Equal("S- Venda p/ Export.", line.UsageName);
        Assert.Equal(_contract.Key, line.SalesContractKey);
        Assert.Equal(_release.Key, line.SalesShipmentReleaseKey);
        Assert.Equal("KG", line.UnitOfMeasureCode);
    }

    [Fact]
    public async Task Links_with_all_the_effects_of_the_manual_billing()
    {
        await SeedAsync();
        await AddSapInvoiceAsync();

        var created = await LinkAsync();

        var load = await _db.Context.ShipmentLoads.AsNoTracking().SingleAsync();
        Assert.Equal(ShipmentLoadStatus.Invoiced, load.Status);
        Assert.Equal(Quantity, load.InvoicedQuantity);

        var release = await _db.Context.SalesShipmentReleases.AsNoTracking().SingleAsync();
        Assert.Equal(Quantity, release.ShippedQuantity);

        Assert.NotEmpty(await _db.Context.SalesContractsAllocations.AsNoTracking().ToListAsync());

        var movement = await _db.Context.ShipmentLoadMovements.AsNoTracking().SingleAsync();
        Assert.Equal(ShipmentLoadMovementType.Billed, movement.MovementType);
        Assert.Equal(Assert.Single(created).Key, movement.SalesInvoiceKey);
    }


    [Fact]
    public async Task Freight_comes_from_the_load_and_delivery_location_from_the_release()
    {
        await SeedAsync(freightPrice: 12.5m);
        await AddSapInvoiceAsync();

        await LinkAsync();

        var saved = await _db.Context.SalesInvoices.AsNoTracking().SingleAsync();
        Assert.Equal(12.5m, saved.FreightCostStandard);
        Assert.Equal("LOCAL77", saved.DeliveryCardCode);
    }

    [Fact]
    public async Task Load_without_freight_gives_zero_freight_standard()
    {
        await SeedAsync(freightPrice: null);
        await AddSapInvoiceAsync();

        await LinkAsync();

        Assert.Equal(0m, (await _db.Context.SalesInvoices.AsNoTracking().SingleAsync()).FreightCostStandard);
    }

    [Fact]
    public async Task Without_fiscal_addon_key_links_with_null_access_key()
    {
        await SeedAsync();
        await AddSapInvoiceAsync();

        await LinkAsync();

        Assert.Null((await _db.Context.SalesInvoices.AsNoTracking().SingleAsync()).ChaveNFe);
    }

    [Fact]
    public async Task With_fiscal_addon_the_authorized_key_is_stored()
    {
        await SeedAsync();
        await AddSapInvoiceAsync();
        var key = new string('1', 44);
        var fiscal = new FakeFiscal(new SapFiscalDocumentInfo(1, key, true, false, null));

        await LinkAsync(Service(fiscal: fiscal));

        Assert.Equal(key, (await _db.Context.SalesInvoices.AsNoTracking().SingleAsync()).ChaveNFe);
    }

    [Fact]
    public async Task Outside_sapb1_is_refused()
    {
        await SeedAsync();
        await AddSapInvoiceAsync();

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => LinkAsync(Service("STANDALONE")));

        Assert.Equal("Disponível somente com integração SAP Business One.", ex.Message);
        await AssertNothingPersistedAsync();
    }

    [Fact]
    public async Task Missing_load_is_not_found()
    {
        await SeedAsync();
        await AddSapInvoiceAsync();

        await Assert.ThrowsAsync<NotFoundException>(() =>
            Service().ExecuteAsync(Guid.NewGuid(), [1], _release.Key, "tester"));
    }

    [Fact]
    public async Task Invoice_cancelled_in_sap_between_listing_and_linking_is_no_longer_available()
    {
        await SeedAsync();
        await AddSapInvoiceAsync(canceled: "Y");

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => LinkAsync());

        Assert.Equal("A NF do SAP 000198105 não está mais disponível para vínculo.", ex.Message);
        await AssertNothingPersistedAsync();
    }

    [Fact]
    public async Task Unknown_invoice_is_no_longer_available()
    {
        await SeedAsync();

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => LinkAsync(null, 99));

        Assert.Equal("A NF do SAP 99 não está mais disponível para vínculo.", ex.Message);
    }

    [Fact]
    public async Task Invoice_already_linked_to_an_active_document_is_refused_naming_it()
    {
        await SeedAsync();
        await AddSapInvoiceAsync();
        _db.Context.SalesInvoices.Add(new SalesInvoice
        {
            Key = Guid.NewGuid(),
            InvoiceNumber = "000321",
            CardCode = CardCode,
            BranchCode = SapBranch,
            InvoiceStatus = InvoiceStatus.Confirmed,
            TaxDocumentNumber = "000198105",
            SapDocEntry = 1,
            SapDocNum = 1001,
        });
        await _db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => LinkAsync());

        Assert.Equal("A NF 000198105 do SAP já está vinculada ao documento de saída 000321.", ex.Message);
        Assert.Equal(1, await InvoiceCountAsync());
    }

    [Fact]
    public async Task Invoice_linked_only_to_a_cancelled_document_can_be_linked_again()
    {
        await SeedAsync();
        await AddSapInvoiceAsync();
        _db.Context.SalesInvoices.Add(new SalesInvoice
        {
            Key = Guid.NewGuid(),
            InvoiceNumber = "000321",
            CardCode = CardCode,
            BranchCode = SapBranch,
            InvoiceStatus = InvoiceStatus.Cancelled,
            SapDocEntry = 1,
        });
        await _db.SaveChangesAsync();

        await LinkAsync();

        Assert.Equal(2, await InvoiceCountAsync());
    }

    [Fact]
    public async Task Contract_customer_different_from_the_sap_customer_links_with_the_customer_of_the_sap_invoice()
    {
        // Contrato num cliente, faturamento em outro (instrução da trading): o documento de saída obedece a NF;
        // a baixa de saldo continua no contrato da liberação escolhida.
        await SeedAsync(releaseCard: "C0002");
        await AddSapInvoiceAsync();

        await LinkAsync();

        var saved = await _db.Context.SalesInvoices.AsNoTracking().Include(i => i.Items).SingleAsync();
        Assert.Equal(CardCode, saved.CardCode);
        Assert.Equal(InvoiceStatus.Confirmed, saved.InvoiceStatus);
        var line = Assert.Single(saved.Items);
        Assert.Equal(_contract.Key, line.SalesContractKey);
        Assert.Equal(_release.Key, line.SalesShipmentReleaseKey);

        var release = await _db.Context.SalesShipmentReleases.AsNoTracking().SingleAsync();
        Assert.Equal(Quantity, release.ShippedQuantity);
        Assert.Equal("C0002", (await _db.Context.SalesContracts.AsNoTracking().SingleAsync()).CardCode);
    }

    [Theory]
    [InlineData(ReleaseStatus.Cancelled)]
    [InlineData(ReleaseStatus.Completed)]
    [InlineData(ReleaseStatus.Paused)]
    [InlineData(ReleaseStatus.Pending)]
    public async Task Release_that_is_not_active_is_refused(ReleaseStatus status)
    {
        await SeedAsync(releaseStatus: status);
        await AddSapInvoiceAsync();

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => LinkAsync());

        Assert.Equal("A liberação de entrega informada não está disponível para esta carga.", ex.Message);
        await AssertNothingPersistedAsync();
    }

    [Fact]
    public async Task Release_of_another_item_is_refused()
    {
        await SeedAsync(contractItem: "MILHO");
        await AddSapInvoiceAsync();

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => LinkAsync());

        Assert.Equal("A liberação de entrega informada não está disponível para esta carga.", ex.Message);
        await AssertNothingPersistedAsync();
    }

    [Fact]
    public async Task Unknown_release_is_refused()
    {
        await SeedAsync();
        await AddSapInvoiceAsync();

        var ex = await Assert.ThrowsAsync<ApplicationException>(() =>
            Service().ExecuteAsync(_load.Key, [1], Guid.NewGuid(), "tester"));

        Assert.Equal("A liberação de entrega informada não está disponível para esta carga.", ex.Message);
    }

    [Fact]
    public async Task Number_and_series_already_used_in_the_branch_are_refused()
    {
        await SeedAsync();
        await AddSapInvoiceAsync();
        _db.Context.SalesInvoices.Add(new SalesInvoice
        {
            Key = Guid.NewGuid(),
            InvoiceNumber = "000500",
            CardCode = CardCode,
            BranchCode = SapBranch,
            InvoiceStatus = InvoiceStatus.Confirmed,
            TaxDocumentNumber = "000198105",
            TaxDocumentSeries = "001",
        });
        await _db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => LinkAsync());

        Assert.Equal(
            "Nota fiscal 000198105, série 001 já informada no documento de saída 000500.", ex.Message);
        Assert.Equal(1, await InvoiceCountAsync());
    }

    /// <summary>
    /// Simula a corrida de dois vínculos da mesma NF: o InMemory não aplica o índice único filtrado
    /// em <c>SapDocEntry</c>, então o interceptor faz o "outro usuário" gravar primeiro e derruba o
    /// SaveChanges do documento confirmado com <see cref="DbUpdateException"/>.
    /// </summary>
    private sealed class RaceInterceptor(string dbName) : SaveChangesInterceptor
    {
        private bool _fired;

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken ct = default)
        {
            var context = eventData.Context!;
            var mine = context.ChangeTracker.Entries<SalesInvoice>()
                .Select(e => e.Entity)
                .FirstOrDefault(i => i.SapDocEntry != null);

            if (!_fired && mine is not null)
            {
                _fired = true;
                using var other = new AppDbContext(TestDb.Options(dbName));
                other.SalesInvoices.Add(new SalesInvoice
                {
                    Key = Guid.NewGuid(),
                    InvoiceNumber = "000777",
                    CardCode = CardCode,
                    BranchCode = SapBranch,
                    InvoiceStatus = InvoiceStatus.Confirmed,
                    TaxDocumentNumber = "000198105",
                    SapDocEntry = mine.SapDocEntry,
                });
                other.SaveChanges();

                throw new DbUpdateException("Cannot insert duplicate key row in object 'SALES_INVOICES'.");
            }

            return base.SavingChangesAsync(eventData, result, ct);
        }
    }

    [Fact]
    public async Task Unique_index_violation_from_a_concurrent_link_is_translated_to_the_already_linked_message()
    {
        var dbName = Guid.NewGuid().ToString();
        var test = new ShipmentLoadsLinkSapInvoiceServiceTests(new RaceInterceptor(dbName), dbName);
        await test.SeedAsync();
        await test.AddSapInvoiceAsync();

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => test.LinkAsync());

        Assert.Equal("A NF 000198105 do SAP já está vinculada ao documento de saída 000777.", ex.Message);
    }

    // ---------- GAC-1226 rodada 2: várias NFs na mesma carga ----------

    [Fact]
    public async Task Two_invoices_summing_the_balance_create_one_document_each_in_input_order()
    {
        await SeedAsync();
        await AddSapInvoiceAsync(1, quantity: 60_000m);
        await AddSapInvoiceAsync(2, quantity: 30_000m);

        var created = await LinkAsync(null, 2, 1);

        Assert.Equal(2, created.Count);
        Assert.Equal(2, created[0].SapDocEntry);
        Assert.Equal(1, created[1].SapDocEntry);
        Assert.NotEqual(created[0].InvoiceNumber, created[1].InvoiceNumber);

        var saved = await _db.Context.SalesInvoices.AsNoTracking().Include(i => i.Items).ToListAsync();
        Assert.Equal(2, saved.Count);
        var first = saved.Single(i => i.SapDocEntry == 1);
        var second = saved.Single(i => i.SapDocEntry == 2);
        Assert.Equal("000198105", first.TaxDocumentNumber);
        Assert.Equal("000198106", second.TaxDocumentNumber);
        Assert.Equal(1001, first.SapDocNum);
        Assert.Equal(1002, second.SapDocNum);
        Assert.Equal(60_000m, Assert.Single(first.Items).Quantity);
        Assert.Equal(30_000m, Assert.Single(second.Items).Quantity);
        Assert.All(saved, i => Assert.Equal(InvoiceStatus.Confirmed, i.InvoiceStatus));

        var load = await _db.Context.ShipmentLoads.AsNoTracking().SingleAsync();
        Assert.Equal(ShipmentLoadStatus.Invoiced, load.Status);
        Assert.Equal(Quantity, load.InvoicedQuantity);

        var release = await _db.Context.SalesShipmentReleases.AsNoTracking().SingleAsync();
        Assert.Equal(Quantity, release.ShippedQuantity);

        Assert.NotEmpty(await _db.Context.SalesContractsAllocations.AsNoTracking().ToListAsync());

        var movements = await _db.Context.ShipmentLoadMovements.AsNoTracking().ToListAsync();
        Assert.Equal(2, movements.Count);
        Assert.All(movements, m => Assert.Equal(ShipmentLoadMovementType.Billed, m.MovementType));
        Assert.Equal(
            saved.Select(i => i.Key).OrderBy(k => k),
            movements.Select(m => m.SalesInvoiceKey!.Value).OrderBy(k => k));

        // Uma transação para o lote inteiro: o orquestrador não comita por documento.
        Assert.Equal(["Begin", "Commit"], _recorder!.Log);
    }

    [Fact]
    public async Task One_invoice_equal_to_the_balance_still_links_in_one_transaction()
    {
        await SeedAsync();
        await AddSapInvoiceAsync();

        var created = await LinkAsync();

        Assert.Single(created);
        Assert.Equal(["Begin", "Commit"], _recorder!.Log);
    }

    // ---------- GAC-1226: vínculo com divergência (carga de 30.000, contrato fixo a R$ 2,00/kg) ----------

    private const decimal DivergentLoad = 30_000m;

    private Task SeedDivergenceAsync(CurrencyType currency = CurrencyType.Brl) =>
        SeedAsync(total: DivergentLoad, contractPrice: 2m, currency: currency);

    private async Task<List<SalesInvoiceDivergence>> DivergencesAsync() =>
        await _db.Context.SalesInvoiceDivergences.AsNoTracking().ToListAsync();

    private async Task<ShipmentLoad> SavedLoadAsync() =>
        await _db.Context.ShipmentLoads.AsNoTracking().SingleAsync(l => l.Key == _load.Key);

    [Fact]
    public async Task Matching_invoice_links_as_normal_without_a_divergence_record()
    {
        await SeedDivergenceAsync();
        await AddSapInvoiceAsync(quantity: 30_000m, price: 2m, lineTotal: 60_000.00m);

        var created = await LinkAsync();

        var saved = await _db.Context.SalesInvoices.AsNoTracking().SingleAsync();
        Assert.Equal(Assert.Single(created).Key, saved.Key);
        Assert.Equal(SapLinkType.Normal, saved.SapLinkType);
        Assert.Empty(await DivergencesAsync());
        Assert.Equal(ShipmentLoadStatus.Invoiced, (await SavedLoadAsync()).Status);
    }

    [Fact]
    public async Task Smaller_invoice_without_confirmation_is_refused_and_writes_nothing()
    {
        await SeedDivergenceAsync();
        await AddSapInvoiceAsync(quantity: 29_800m, price: 2m);

        var ex = await Assert.ThrowsAsync<SapLinkDivergenceException>(() => LinkAsync());

        Assert.Equal(
            "Valor(es) e qtde(s) da(s) NF(s) diferente(s) do contrato, deseja finalizar o faturamento?", ex.Message);
        Assert.Equal(SapLinkType.QuantityAndValueDivergent, ex.Assessment.Type);
        await AssertNothingPersistedAsync();
        Assert.Empty(await DivergencesAsync());
        Assert.Equal(["Begin", "Rollback"], _recorder!.Log);
    }

    [Fact]
    public async Task Smaller_invoice_confirmed_links_and_closes_the_load()
    {
        await SeedDivergenceAsync();
        await AddSapInvoiceAsync(quantity: 29_800m, price: 2m);

        var created = await ConfirmedLinkAsync();

        var saved = await _db.Context.SalesInvoices.AsNoTracking().Include(i => i.Items).SingleAsync();
        Assert.Equal(Assert.Single(created).Key, saved.Key);
        Assert.Equal(29_800m, Assert.Single(saved.Items).Quantity);
        Assert.Equal(SapLinkType.QuantityAndValueDivergent, saved.SapLinkType);

        var divergence = Assert.Single(await DivergencesAsync());
        Assert.Equal(saved.Key, divergence.SalesInvoiceKey);
        Assert.Equal(_load.Key, divergence.ShipmentLoadKey);
        Assert.Equal(SapLinkType.QuantityAndValueDivergent, divergence.LinkType);
        Assert.Equal(SalesInvoiceDivergenceStatus.Open, divergence.Status);
        Assert.Equal("tester", divergence.CreatedBy);
        Assert.Equal(30_000m, divergence.EffectiveQuantity);
        Assert.Equal(29_800m, divergence.InvoiceQuantity);
        Assert.Equal(200m, divergence.QuantityDifference);
        Assert.Equal(60_000.00m, divergence.EffectiveValue);
        Assert.Equal(59_600.00m, divergence.InvoiceValue);
        Assert.Equal(400.00m, divergence.ValueDifference);

        var load = await SavedLoadAsync();
        Assert.Equal(29_800m, load.InvoicedQuantity);
        Assert.Equal(200m, load.InvoiceDivergenceQuantity);
        Assert.Equal(0m, load.AvailableQuantity);
        Assert.Equal(ShipmentLoadStatus.Invoiced, load.Status);

        // O contrato é baixado pela quantidade da NF.
        Assert.Equal(29_800m, await _db.Context.SalesContractsAllocations.AsNoTracking().SumAsync(a => a.Volume));
        Assert.Equal(["Begin", "Commit"], _recorder!.Log);
    }

    [Fact]
    public async Task Larger_invoice_confirmed_links_with_a_negative_divergence()
    {
        await SeedDivergenceAsync();
        await AddSapInvoiceAsync(quantity: 30_150m, price: 2m);

        await ConfirmedLinkAsync();

        var divergence = Assert.Single(await DivergencesAsync());
        Assert.Equal(-150m, divergence.QuantityDifference);
        Assert.Equal(SapLinkType.QuantityAndValueDivergent, divergence.LinkType);

        var load = await SavedLoadAsync();
        Assert.Equal(30_150m, load.InvoicedQuantity);
        Assert.Equal(-150m, load.InvoiceDivergenceQuantity);
        Assert.Equal(ShipmentLoadStatus.Invoiced, load.Status);
    }

    [Fact]
    public async Task Value_divergence_confirmed_records_type_one_with_zero_quantity_difference()
    {
        await SeedDivergenceAsync();
        await AddSapInvoiceAsync(quantity: 30_000m, price: 2.01m, lineTotal: 60_300.00m);

        var unconfirmed = await Assert.ThrowsAsync<SapLinkDivergenceException>(() => LinkAsync());
        Assert.Equal(
            "Valor(es) da(s) NF(s) diferente(s) do contrato, deseja finalizar o faturamento?", unconfirmed.Message);

        await ConfirmedLinkAsync();

        Assert.Equal(SapLinkType.ValueDivergent,
            (await _db.Context.SalesInvoices.AsNoTracking().SingleAsync()).SapLinkType);
        var divergence = Assert.Single(await DivergencesAsync());
        Assert.Equal(SapLinkType.ValueDivergent, divergence.LinkType);
        Assert.Equal(0m, divergence.QuantityDifference);
        Assert.Equal(-300.00m, divergence.ValueDifference);
        Assert.Equal(ShipmentLoadStatus.Invoiced, (await SavedLoadAsync()).Status);
    }

    [Fact]
    public async Task Two_invoices_get_one_record_each_and_the_shares_sum_the_effective_quantity()
    {
        await SeedDivergenceAsync();
        await AddSapInvoiceAsync(1, quantity: 10_000m, price: 2m);
        await AddSapInvoiceAsync(2, quantity: 19_800m, price: 2m);

        var created = await ConfirmedLinkAsync(1, 2);

        Assert.Equal(2, created.Count);
        var divergences = await DivergencesAsync();
        Assert.Equal(2, divergences.Count);
        Assert.Equal(
            created.Select(c => c.Key).OrderBy(k => k), divergences.Select(d => d.SalesInvoiceKey).OrderBy(k => k));
        Assert.Equal(30_000m, divergences.Sum(d => d.EffectiveQuantity));
        Assert.Equal(200m, divergences.Sum(d => d.QuantityDifference));
        Assert.Equal(10_000m, divergences.Single(d => d.SalesInvoiceKey == created[0].Key).InvoiceQuantity);
        Assert.Equal(19_800m, divergences.Single(d => d.SalesInvoiceKey == created[1].Key).InvoiceQuantity);
        Assert.All(divergences, d => Assert.Equal(SapLinkType.QuantityAndValueDivergent, d.LinkType));

        var load = await SavedLoadAsync();
        Assert.Equal(ShipmentLoadStatus.Invoiced, load.Status);
        Assert.Equal(0m, load.AvailableQuantity);
    }

    [Fact]
    public async Task Dollar_contract_classifies_by_quantity_only()
    {
        await SeedDivergenceAsync(CurrencyType.Usd);
        await AddSapInvoiceAsync(1, quantity: 30_000m, price: 7m);

        await LinkAsync();

        Assert.Equal(SapLinkType.Normal, (await _db.Context.SalesInvoices.AsNoTracking().SingleAsync()).SapLinkType);
        Assert.Empty(await DivergencesAsync());

        // Segunda carga do mesmo contrato, agora com NF menor que o saldo.
        var second = new ShipmentLoad
        {
            Key = Guid.NewGuid(), Code = "CG000011", BranchCode = SapBranch, ItemCode = Item,
            ItemName = "SOJA EM GRAOS", UnitOfMeasureCode = "KG", CardCode = CardCode, TruckCode = "ABC1D23",
            TruckDriverCode = "MOT01", CarrierCardCode = Carrier, CarrierName = "TRANSPORTES YOKOTOBI LTDA",
            TotalQuantity = DivergentLoad, Status = ShipmentLoadStatus.Open,
        };
        _db.Context.ShipmentLoads.Add(second);
        await _db.SaveChangesAsync();
        await AddSapInvoiceAsync(2, quantity: 29_800m, price: 7m);

        await Service().ExecuteAsync(second.Key, [2], _release.Key, "tester", confirmDivergence: true);

        var divergence = Assert.Single(await DivergencesAsync());
        Assert.Equal(SapLinkType.QuantityAndValueDivergent, divergence.LinkType);
        Assert.Equal(200m, divergence.QuantityDifference);
        Assert.Null(divergence.EffectiveValue);
        Assert.Null(divergence.ValueDifference);
    }

    [Fact]
    public async Task Load_without_balance_is_refused()
    {
        await SeedDivergenceAsync();
        await AddSapInvoiceAsync(1, quantity: 30_000m, price: 2m);
        await AddSapInvoiceAsync(2, quantity: 1_000m, price: 2m);
        await LinkAsync(null, 1);

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => LinkAsync(null, 2));

        Assert.Equal("A carga CG000010 não tem saldo a faturar.", ex.Message);
        Assert.Equal(1, await InvoiceCountAsync());
    }

    [Fact]
    public async Task Preview_returns_the_assessment_and_writes_nothing()
    {
        await SeedDivergenceAsync();
        await AddSapInvoiceAsync(quantity: 29_800m, price: 2m);

        var assessment = await Service().PreviewAsync(_load.Key, [1], _release.Key);

        Assert.Equal(SapLinkType.QuantityAndValueDivergent, assessment.Type);
        Assert.Equal(
            "Valor(es) e qtde(s) da(s) NF(s) diferente(s) do contrato, deseja finalizar o faturamento?",
            assessment.Message);
        Assert.Equal(30_000m, assessment.EffectiveQuantity);
        Assert.Equal(29_800m, assessment.InvoiceQuantity);
        Assert.Equal(200m, assessment.QuantityDifference);
        Assert.Equal(60_000.00m, assessment.EffectiveValue);
        Assert.Equal(59_600.00m, assessment.InvoiceValue);
        Assert.Equal(400.00m, assessment.ValueDifference);

        await AssertNothingPersistedAsync();
        Assert.Empty(await DivergencesAsync());
        Assert.Empty(_recorder!.Log);
    }

    [Fact]
    public async Task Preview_outside_sapb1_is_refused()
    {
        await SeedDivergenceAsync();
        await AddSapInvoiceAsync(quantity: 29_800m, price: 2m);

        var ex = await Assert.ThrowsAsync<ApplicationException>(() =>
            Service("STANDALONE").PreviewAsync(_load.Key, [1], _release.Key));

        Assert.Equal("Disponível somente com integração SAP Business One.", ex.Message);
    }

    [Fact]
    public async Task Invoice_of_another_customer_is_no_longer_available()
    {
        await SeedAsync();
        await AddSapInvoiceAsync(cardCode: "C0099");

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => LinkAsync());

        Assert.Equal("A NF do SAP 000198105 não está mais disponível para vínculo.", ex.Message);
        await AssertNothingPersistedAsync();
    }

    [Fact]
    public async Task Empty_list_is_refused()
    {
        await SeedAsync();

        var ex = await Assert.ThrowsAsync<ApplicationException>(() =>
            Service().ExecuteAsync(_load.Key, [], _release.Key, "tester"));

        Assert.Equal("Selecione ao menos uma nota fiscal do SAP.", ex.Message);
    }

    [Fact]
    public async Task Repeated_doc_entry_is_refused()
    {
        await SeedAsync();
        await AddSapInvoiceAsync(1, quantity: 45_000m);

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => LinkAsync(null, 1, 1));

        Assert.Equal("A mesma nota fiscal do SAP foi informada mais de uma vez.", ex.Message);
        await AssertNothingPersistedAsync();
    }

    [Fact]
    public async Task One_invoice_cancelled_in_sap_blocks_the_whole_batch()
    {
        await SeedAsync();
        await AddSapInvoiceAsync(1, quantity: 60_000m);
        await AddSapInvoiceAsync(2, quantity: 30_000m, canceled: "Y");

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => LinkAsync(null, 1, 2));

        Assert.Equal("A NF do SAP 000198106 não está mais disponível para vínculo.", ex.Message);
        await AssertNothingPersistedAsync();
    }

    [Fact]
    public async Task One_invoice_already_linked_blocks_the_whole_batch()
    {
        await SeedAsync();
        await AddSapInvoiceAsync(1, quantity: 60_000m);
        await AddSapInvoiceAsync(2, quantity: 30_000m);
        _db.Context.SalesInvoices.Add(new SalesInvoice
        {
            Key = Guid.NewGuid(), InvoiceNumber = "000321", CardCode = CardCode, BranchCode = SapBranch,
            InvoiceStatus = InvoiceStatus.Confirmed, TaxDocumentNumber = "000198106", SapDocEntry = 2,
        });
        await _db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => LinkAsync(null, 1, 2));

        Assert.Equal("A NF 000198106 do SAP já está vinculada ao documento de saída 000321.", ex.Message);
        Assert.Equal(1, await InvoiceCountAsync());
    }

    /// <summary>
    /// Prova de atomicidade: duas NFs do SAP com o mesmo número/série — a segunda cai na trava de
    /// duplicidade DEPOIS de o primeiro documento ter sido gravado. O InMemory não desfaz gravação, então
    /// a prova é o protocolo: uma única transação, nenhum commit e rollback na falha (no SQL Server o
    /// rollback apaga o primeiro documento e todos os seus efeitos).
    /// </summary>
    [Fact]
    public async Task Failure_on_the_second_document_rolls_back_the_whole_batch_without_committing()
    {
        await SeedAsync();
        await AddSapInvoiceAsync(1, quantity: 60_000m, serial: 500);
        await AddSapInvoiceAsync(2, quantity: 30_000m, serial: 500);

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => LinkAsync(null, 1, 2));

        Assert.StartsWith("Nota fiscal 000000500, série 001 já informada no documento de saída", ex.Message);
        Assert.Equal(["Begin", "Rollback"], _recorder!.Log);
    }

    /// <summary>Falha do orquestrador no segundo documento (simulada no SaveChanges): mesmo protocolo.</summary>
    private sealed class FailOnSecondInvoiceInterceptor : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken ct = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<SalesInvoice>()
                .Any(e => e.State == EntityState.Added && e.Entity.Items.Any(i => i.Quantity == 30_000m)))
                throw new InvalidOperationException("falha simulada no segundo documento");

            return base.SavingChangesAsync(eventData, result, ct);
        }
    }

    [Fact]
    public async Task Orchestrator_failure_on_the_second_document_rolls_back_without_committing()
    {
        var dbName = Guid.NewGuid().ToString();
        var test = new ShipmentLoadsLinkSapInvoiceServiceTests(new FailOnSecondInvoiceInterceptor(), dbName);
        await test.SeedAsync();
        await test.AddSapInvoiceAsync(1, quantity: 60_000m);
        await test.AddSapInvoiceAsync(2, quantity: 30_000m);

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => test.LinkAsync(null, 1, 2));

        Assert.Equal("falha simulada no segundo documento", ex.Message);
        Assert.Equal(["Begin", "Rollback"], test._recorder!.Log);
    }

    /// <summary>Falha inesperada de gravação (DbUpdateException) no segundo documento.</summary>
    private sealed class DbUpdateFailureOnSecondInvoiceInterceptor : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken ct = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<SalesInvoice>()
                .Any(e => e.State == EntityState.Added && e.Entity.Items.Any(i => i.Quantity == 30_000m)))
                throw new DbUpdateException(
                    "An error occurred while saving the entity changes. See the inner exception for details.");

            return base.SavingChangesAsync(eventData, result, ct);
        }
    }

    [Fact]
    public async Task Unexpected_db_update_failure_surfaces_as_a_clear_business_error()
    {
        var dbName = Guid.NewGuid().ToString();
        var test = new ShipmentLoadsLinkSapInvoiceServiceTests(new DbUpdateFailureOnSecondInvoiceInterceptor(), dbName);
        await test.SeedAsync();
        await test.AddSapInvoiceAsync(1, quantity: 60_000m);
        await test.AddSapInvoiceAsync(2, quantity: 30_000m);

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => test.LinkAsync(null, 1, 2));

        Assert.Equal(
            "Não foi possível vincular as notas fiscais do SAP. Nenhuma alteração foi gravada. Tente novamente.",
            ex.Message);
        Assert.Equal(["Begin", "Rollback"], test._recorder!.Log);
    }

    [Fact]
    public async Task Guard_business_error_keeps_its_own_message()
    {
        await SeedAsync();
        await AddSapInvoiceAsync(1, quantity: 60_000m, serial: 500);
        await AddSapInvoiceAsync(2, quantity: 30_000m, serial: 500);

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => LinkAsync(null, 1, 2));

        Assert.StartsWith("Nota fiscal 000000500", ex.Message);
    }

    [Fact]
    public async Task Orchestrator_keeps_the_original_exception_as_inner_exception()
    {
        var dbName = Guid.NewGuid().ToString();
        var test = new ShipmentLoadsLinkSapInvoiceServiceTests(new FailOnSecondInvoiceInterceptor(), dbName);
        await test.SeedAsync();
        await test.AddSapInvoiceAsync(1, quantity: 60_000m);
        await test.AddSapInvoiceAsync(2, quantity: 30_000m);

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => test.LinkAsync(null, 1, 2));

        Assert.NotNull(ex.InnerException);
        var root = ex.InnerException;
        while (root!.InnerException != null) root = root.InnerException;
        Assert.IsType<InvalidOperationException>(root);
    }

    [Fact]
    public void Parses_a_valid_list_of_doc_entries()
    {
        Assert.True(ShipmentLoadsLinkSapInvoiceService.TryParseDocEntries(new List<int> { 5, 7 }, out var entries));
        Assert.Equal([5, 7], entries);
    }

    [Fact]
    public void Rejects_null_zero_negative_and_non_collection_doc_entries()
    {
        Assert.False(ShipmentLoadsLinkSapInvoiceService.TryParseDocEntries(new List<int?> { 5, null }, out _));
        Assert.False(ShipmentLoadsLinkSapInvoiceService.TryParseDocEntries(new List<int> { 5, 0 }, out _));
        Assert.False(ShipmentLoadsLinkSapInvoiceService.TryParseDocEntries(new List<int> { -3 }, out _));
        Assert.False(ShipmentLoadsLinkSapInvoiceService.TryParseDocEntries("12", out _));
        Assert.False(ShipmentLoadsLinkSapInvoiceService.TryParseDocEntries(null, out _));
    }
}
