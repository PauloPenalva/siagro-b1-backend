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
            config);
    }

    private async Task SeedAsync(string? releaseCard = null, ReleaseStatus releaseStatus = ReleaseStatus.Actived,
        string contractItem = Item, decimal? freightPrice = 12.5m)
    {
        _load = new ShipmentLoad
        {
            Key = Guid.NewGuid(),
            Code = "CG000010",
            BranchCode = SapBranch,
            ItemCode = Item,
            ItemName = "SOJA EM GRAOS",
            UnitOfMeasureCode = "KG",
            TruckCode = "ABC1D23",
            TruckDriverCode = "MOT01",
            CarrierCardCode = Carrier,
            CarrierName = "TRANSPORTES YOKOTOBI LTDA",
            TotalQuantity = Quantity,
            FreightPrice = freightPrice,
            Status = ShipmentLoadStatus.Open,
        };

        _contract = SalesContractsAllocationTestSupport.NewContract(
            totalVolume: 1_000_000m, cardCode: releaseCard ?? CardCode, itemCode: contractItem);
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
        int? serial = null)
    {
        _sap.SapInvoices.Add(new SapInvoice
        {
            DocEntry = docEntry,
            DocNum = docEntry + 1000,
            DocDate = new DateTime(2026, 11, 15),
            CardCode = cardCode,
            CardName = "CLIENTE TESTE",
            BPLId = 3,
            Canceled = canceled,
            Serial = serial ?? 198104 + docEntry,
            SeriesStr = "1",
            Model = "39",
        });
        _sap.SapInvoiceLines.Add(new SapInvoiceLine
        {
            DocEntry = docEntry, LineNum = 0, ItemCode = Item, Quantity = quantity,
            Price = 2.5m, LineTotal = quantity * 2.5m, CfopCode = "5101",
        });
        _sap.SapInvoiceTransports.Add(new SapInvoiceTransport
            { DocEntry = docEntry, Carrier = carrier, Incoterms = incoterms });
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
    public async Task Document_carries_the_loads_carrier_even_when_the_sap_invoice_has_another()
    {
        await SeedAsync();
        await AddSapInvoiceAsync(carrier: "OUTRA01");

        await LinkAsync();

        var saved = await _db.Context.SalesInvoices.AsNoTracking().SingleAsync();
        Assert.Equal(Carrier, saved.TruckingCompanyCode);
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
    public async Task Contract_customer_different_from_the_sap_customer_is_refused()
    {
        await SeedAsync(releaseCard: "C0002");
        await AddSapInvoiceAsync();

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => LinkAsync());

        Assert.Equal(
            "O cliente da NF do SAP (C0001) é diferente do cliente do contrato da liberação.", ex.Message);
        await AssertNothingPersistedAsync();
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

    [Theory]
    [InlineData(50_000, 30_000, "80.000,000")]
    [InlineData(60_000, 40_000, "100.000,000")]
    [InlineData(80_000, 0, "80.000,000")]
    [InlineData(95_000, 0, "95.000,000")]
    public async Task Sum_different_from_the_balance_is_refused_and_nothing_is_persisted(
        int q1, int q2, string sum)
    {
        await SeedAsync();
        await AddSapInvoiceAsync(1, quantity: q1);
        int[] entries = [1];
        if (q2 > 0)
        {
            await AddSapInvoiceAsync(2, quantity: q2);
            entries = [1, 2];
        }

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => LinkAsync(null, entries));

        Assert.Equal(
            $"A soma das notas fiscais do SAP ({sum}) é diferente do saldo da carga CG000010 (90.000,000). " +
            "O vínculo exige que a soma seja igual ao saldo.", ex.Message);
        await AssertNothingPersistedAsync();
    }

    [Fact]
    public async Task Invoices_of_different_customers_are_refused()
    {
        await SeedAsync();
        await AddSapInvoiceAsync(1, quantity: 60_000m);
        await AddSapInvoiceAsync(2, quantity: 30_000m, cardCode: "C0002");

        var ex = await Assert.ThrowsAsync<ApplicationException>(() => LinkAsync(null, 1, 2));

        Assert.Equal("As notas fiscais selecionadas devem ser do mesmo cliente.", ex.Message);
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
}
