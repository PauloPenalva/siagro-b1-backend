using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SiagroB1.Application.Services.SalesInvoices;
using SiagroB1.Application.Services.SAP;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Entities.SAP;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Infra;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Tests.ShipmentLoads;

/// <summary>
/// "Vincular NF de devolução" e "Desvincular NF de devolução" (GAC-1226): a NF de devolução do SAP (A/R Credit
/// Memo) entra e sai do documento de devolução já confirmado sem tocar em nada físico.
/// </summary>
public class ShipmentLoadsSapCreditMemoLinkTests
{
    private const string Item = "SOJA";
    private const string Customer = "C0001";
    private const decimal Quantity = 30_000m;
    private const string AccessKey = "35261112345678000199550010000045121000012345";

    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();
    private readonly SapErpDbContext _sap = new(
        new DbContextOptionsBuilder<SapErpDbContext>().UseInMemoryDatabase($"sap-{Guid.NewGuid()}").Options);

    private ShipmentLoad _load = null!;
    private SalesInvoice _origin = null!;
    private SalesInvoice _return = null!;

    private sealed class FakeFiscal(params SapFiscalDocumentInfo[] infos) : ISapFiscalDocumentSource
    {
        public bool IsAvailable => infos.Length > 0;

        public Task<IReadOnlyDictionary<int, SapFiscalDocumentInfo>> GetForInvoicesAsync(
            IReadOnlyCollection<SapInvoiceRef> invoices, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyDictionary<int, SapFiscalDocumentInfo>>(new Dictionary<int, SapFiscalDocumentInfo>());

        public Task<IReadOnlyDictionary<int, SapFiscalDocumentInfo>> GetForCreditMemosAsync(
            IReadOnlyCollection<SapInvoiceRef> creditMemos, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyDictionary<int, SapFiscalDocumentInfo>>(infos.ToDictionary(i => i.DocEntry));
    }

    private static IConfiguration Config(string erp = "SAPB1") =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Erp"] = erp }).Build();

    private SapLinkableCreditMemosService Finder(bool withKey = true) =>
        new(_db.Context, _sap, withKey
            ? new FakeFiscal(new SapFiscalDocumentInfo(1, AccessKey, true, false, "135"))
            : new FakeFiscal());

    private ShipmentLoadsLinkSapCreditMemoService Link(string erp = "SAPB1", bool withKey = true) =>
        new(_db, Finder(withKey), new SalesInvoiceTaxDocumentDuplicateGuard(_db.Context),
            new SalesInvoicesChangeLogService(_db.Context), Config(erp));

    private ShipmentLoadsUnlinkSapCreditMemoService Unlink(string erp = "SAPB1") =>
        new(_db, new SalesInvoicesChangeLogService(_db.Context), Config(erp));

    /// <summary>Simula a corrida: outro documento fica com a nota entre a validação e a gravação.</summary>
    private sealed class RacingLink(UnitOfWork db, SapLinkableCreditMemosService finder, bool rivalTakesIt)
        : ShipmentLoadsLinkSapCreditMemoService(db, finder, new SalesInvoiceTaxDocumentDuplicateGuard(db.Context),
            new SalesInvoicesChangeLogService(db.Context), Config())
    {
        protected override async Task SaveAsync()
        {
            db.Context.ChangeTracker.Clear();
            if (rivalTakesIt)
            {
                db.Context.SalesInvoices.Add(new SalesInvoice
                {
                    Key = Guid.NewGuid(), CardCode = Customer, BranchCode = "3", InvoiceType = SalesInvoiceType.Return,
                    InvoiceStatus = InvoiceStatus.Confirmed, SapCreditMemoDocEntry = 1,
                });
                await db.SaveChangesAsync();
            }

            throw new DbUpdateException("Cannot insert duplicate key row in object 'dbo.SALES_INVOICES'");
        }
    }

    private async Task SeedAsync(Action<SalesInvoice>? tweakReturn = null)
    {
        _load = new ShipmentLoad
        {
            Key = Guid.NewGuid(), Code = "CG000010", BranchCode = "3", ItemCode = Item, UnitOfMeasureCode = "KG",
            TotalQuantity = 90_000m, InvoicedQuantity = 60_000m, ReturnedToWarehouseQuantity = Quantity,
            Status = ShipmentLoadStatus.PartiallyInvoiced,
        };
        _origin = new SalesInvoice
        {
            Key = Guid.NewGuid(), CardCode = Customer, BranchCode = "3", InvoiceType = SalesInvoiceType.Normal,
            InvoiceStatus = InvoiceStatus.Confirmed, ShipmentLoadKey = _load.Key, SapDocEntry = 500, SapDocNum = 1500,
            TaxDocumentNumber = "000198105", TaxDocumentSeries = "001",
            Items = [new SalesInvoiceItem { ItemCode = Item, UnitOfMeasureCode = "KG", Quantity = 90_000m, ReturnedQuantity = Quantity }],
        };
        _return = new SalesInvoice
        {
            Key = Guid.NewGuid(), CardCode = Customer, BranchCode = "3", InvoiceType = SalesInvoiceType.Return,
            InvoiceStatus = InvoiceStatus.Confirmed, ShipmentLoadKey = _load.Key, SalesInvoiceOriginKey = _origin.Key,
            TaxDocumentKind = TaxDocumentKind.Nfe,
            Items = [new SalesInvoiceItem
            {
                ItemCode = Item, UnitOfMeasureCode = "KG", Quantity = Quantity, Cfop = "5101", UsageCode = 10,
                UsageName = "VENDA",
            }],
        };
        tweakReturn?.Invoke(_return);

        _db.Context.ShipmentLoads.Add(_load);
        _db.Context.SalesInvoices.AddRange(_origin, _return);
        await _db.SaveChangesAsync();
        _db.Context.ChangeTracker.Clear();
    }

    private async Task AddCreditMemoAsync(int docEntry = 1, decimal? quantity = null, int serial = 4512,
        string? cfop = "1201", int? usage = 71)
    {
        _sap.SapCreditMemos.Add(new SapCreditMemo
        {
            DocEntry = docEntry, DocNum = docEntry + 2000, DocDate = new DateTime(2026, 11, 15), CardCode = Customer,
            CardName = "CLIENTE TESTE", BPLId = 3, Canceled = "N", Serial = serial, SeriesStr = "1", Model = "39",
        });
        _sap.SapCreditMemoLines.Add(new SapCreditMemoLine
        {
            DocEntry = docEntry, LineNum = 0, ItemCode = Item, Quantity = quantity ?? Quantity, Price = 2.5m,
            LineTotal = (quantity ?? Quantity) * 2.5m, CfopCode = cfop, Usage = usage, BaseType = 13, BaseEntry = 500,
        });
        if (usage is { } code && !await _sap.Usages.AnyAsync(u => u.Code == code))
            _sap.Usages.Add(new SiagroB1.Domain.Entities.SAP.Usage { Code = code, Name = "DEVOLUCAO DE VENDA" });
        await _sap.SaveChangesAsync();
    }

    private async Task<SalesInvoice> SavedReturnAsync() =>
        await _db.Context.SalesInvoices.AsNoTracking().Include(x => x.Items).SingleAsync(x => x.Key == _return.Key);

    /// <summary>Tudo o que é físico: a carga, o documento de origem e as linhas dele, e os romaneios.</summary>
    private async Task<string> PhysicalSnapshotAsync()
    {
        var ctx = _db.Context;
        var entities = new List<object>();
        entities.AddRange(await ctx.ShipmentLoads.AsNoTracking().ToListAsync());
        entities.AddRange(await ctx.SalesInvoices.AsNoTracking().Where(x => x.Key != _return.Key).ToListAsync());
        entities.AddRange(await ctx.SalesInvoicesItems.AsNoTracking()
            .Where(x => x.SalesInvoiceKey != _return.Key).ToListAsync());
        entities.AddRange(await ctx.StorageTransactions.AsNoTracking().ToListAsync());
        entities.AddRange(await ctx.ShipmentLoadMovements.AsNoTracking().ToListAsync());

        // Só a quantidade e o estado da devolução; os campos fiscais dela são justamente o que o vínculo muda.
        var saved = await SavedReturnAsync();

        return string.Join("\n", entities.Select(e =>
                   e.GetType().Name + ":" + string.Join("|", ctx.Entry(e).Properties
                       .Where(p => p.Metadata.Name != "RowVersion")
                       .Select(p => $"{p.Metadata.Name}={p.CurrentValue}"))))
               + $"\nreturn:{saved.InvoiceStatus}|{saved.InvoiceType}|{saved.Items.Sum(i => i.Quantity)}";
    }

    // ---------- vincular ----------

    [Fact]
    public async Task Link_writes_the_credit_memo_and_its_fiscal_data_on_the_return()
    {
        await SeedAsync();
        await AddCreditMemoAsync();

        await Link().ExecuteAsync(_load.Key, _return.Key, 1, "tester");

        var saved = await SavedReturnAsync();
        Assert.Equal(1, saved.SapCreditMemoDocEntry);
        Assert.Equal(2001, saved.SapCreditMemoDocNum);
        Assert.Null(saved.SapDocEntry); // a coluna da invoice de venda não é usada pela devolução
        Assert.Equal("000004512", saved.TaxDocumentNumber);
        Assert.Equal("001", saved.TaxDocumentSeries);
        Assert.Equal(AccessKey, saved.ChaveNFe);
        Assert.False(saved.WithoutTaxDocument);
        Assert.Equal(InvoiceStatus.Confirmed, saved.InvoiceStatus);
        var line = Assert.Single(saved.Items);
        Assert.Equal("1201", line.Cfop);
        Assert.Equal(71, line.UsageCode);
        Assert.Equal("DEVOLUCAO DE VENDA", line.UsageName);
    }

    [Fact]
    public async Task Link_keeps_cfop_and_usage_when_the_sap_line_has_none()
    {
        await SeedAsync();
        await AddCreditMemoAsync(cfop: null, usage: null);

        await Link().ExecuteAsync(_load.Key, _return.Key, 1, "tester");

        var line = Assert.Single((await SavedReturnAsync()).Items);
        Assert.Equal("5101", line.Cfop);
        Assert.Equal(10, line.UsageCode);
        Assert.Equal("VENDA", line.UsageName);
    }

    [Fact]
    public async Task Link_overrides_a_manual_number_and_the_without_tax_document_flag()
    {
        await SeedAsync(r => { r.WithoutTaxDocument = true; r.TaxDocumentNumber = "777"; r.TaxDocumentSeries = "9"; });
        await AddCreditMemoAsync();

        await Link().ExecuteAsync(_load.Key, _return.Key, 1, "tester");

        var saved = await SavedReturnAsync();
        Assert.False(saved.WithoutTaxDocument);
        Assert.Equal("000004512", saved.TaxDocumentNumber);
        Assert.Equal("001", saved.TaxDocumentSeries);
    }

    [Fact]
    public async Task Link_without_the_fiscal_addon_leaves_the_key_empty()
    {
        await SeedAsync();
        await AddCreditMemoAsync();

        await Link(withKey: false).ExecuteAsync(_load.Key, _return.Key, 1, "tester");

        Assert.Null((await SavedReturnAsync()).ChaveNFe);
    }

    [Fact]
    public async Task Link_is_registered_in_the_change_log()
    {
        await SeedAsync();
        await AddCreditMemoAsync();

        await Link().ExecuteAsync(_load.Key, _return.Key, 1, "tester");

        var log = await _db.Context.SalesInvoicesChangeLogs.AsNoTracking().SingleAsync();
        Assert.Equal(_return.Key, log.SalesInvoiceKey);
        Assert.Equal(ContractChangeLogFields.SapReturnInvoice, log.Field);
        Assert.Null(log.OldValue);
        Assert.Equal("NF 000004512/001 (doc. SAP 2001)", log.NewValue);
        Assert.Equal("tester", log.ChangedBy);
    }

    [Fact]
    public async Task Link_and_unlink_move_nothing_physical()
    {
        await SeedAsync();
        await AddCreditMemoAsync();
        var before = await PhysicalSnapshotAsync();

        await Link().ExecuteAsync(_load.Key, _return.Key, 1, "tester");
        _db.Context.ChangeTracker.Clear();
        var linked = await PhysicalSnapshotAsync();
        await Unlink().ExecuteAsync(_load.Key, _return.Key, "tester");
        _db.Context.ChangeTracker.Clear();
        var unlinked = await PhysicalSnapshotAsync();

        Assert.Equal(before, linked);
        Assert.Equal(before, unlinked);
    }

    [Fact]
    public async Task Link_is_refused_outside_sap_mode()
    {
        await SeedAsync();
        await AddCreditMemoAsync();

        var error = await Assert.ThrowsAsync<ApplicationException>(
            () => Link("STANDALONE").ExecuteAsync(_load.Key, _return.Key, 1, "tester"));

        Assert.Equal(ShipmentLoadsLinkSapCreditMemoService.SapOnlyMessage, error.Message);
        Assert.Null((await SavedReturnAsync()).SapCreditMemoDocEntry);
    }

    [Fact]
    public async Task Link_is_refused_when_the_return_already_has_a_credit_memo()
    {
        await SeedAsync(r => { r.SapCreditMemoDocEntry = 1; r.SapCreditMemoDocNum = 2001; });
        await AddCreditMemoAsync();
        await AddCreditMemoAsync(2, serial: 4513);

        foreach (var docEntry in new[] { 1, 2 })
        {
            var error = await Assert.ThrowsAsync<ApplicationException>(
                () => Link().ExecuteAsync(_load.Key, _return.Key, docEntry, "tester"));
            Assert.Equal(ShipmentLoadsLinkSapCreditMemoService.AlreadyLinkedMessage, error.Message);
        }

        Assert.Equal(1, (await SavedReturnAsync()).SapCreditMemoDocEntry);
    }

    [Fact]
    public async Task Link_is_refused_when_the_credit_memo_is_no_longer_linkable()
    {
        await SeedAsync();

        var error = await Assert.ThrowsAsync<ApplicationException>(
            () => Link().ExecuteAsync(_load.Key, _return.Key, 99, "tester"));

        Assert.Equal(ShipmentLoadsLinkSapCreditMemoService.UnavailableMessage, error.Message);
    }

    [Fact]
    public async Task Link_is_refused_when_the_quantity_differs()
    {
        await SeedAsync();
        await AddCreditMemoAsync(quantity: 29_500m);

        var error = await Assert.ThrowsAsync<ApplicationException>(
            () => Link(withKey: false).ExecuteAsync(_load.Key, _return.Key, 1, "tester"));

        Assert.Equal(
            "A quantidade da NF de devolução (29.500,000) é diferente da quantidade da devolução (30.000,000).",
            error.Message);
        Assert.Null((await SavedReturnAsync()).SapCreditMemoDocEntry);
    }

    [Fact]
    public async Task Link_is_refused_for_a_document_of_another_load()
    {
        await SeedAsync();
        await AddCreditMemoAsync();

        var error = await Assert.ThrowsAsync<BusinessException>(
            () => Link().ExecuteAsync(Guid.NewGuid(), _return.Key, 1, "tester"));

        Assert.Equal(SapLinkableCreditMemosService.NotReturnDocumentMessage, error.Message);
    }

    [Fact]
    public async Task Link_is_refused_when_the_number_already_belongs_to_another_document()
    {
        await SeedAsync();
        await AddCreditMemoAsync();
        _db.Context.SalesInvoices.Add(new SalesInvoice
        {
            Key = Guid.NewGuid(), InvoiceNumber = "DS000099", CardCode = Customer, BranchCode = "3",
            InvoiceStatus = InvoiceStatus.Confirmed, TaxDocumentNumber = "000004512", TaxDocumentSeries = "001",
        });
        await _db.SaveChangesAsync();
        _db.Context.ChangeTracker.Clear();

        var error = await Assert.ThrowsAsync<ApplicationException>(
            () => Link().ExecuteAsync(_load.Key, _return.Key, 1, "tester"));

        Assert.Contains("DS000099", error.Message);
        Assert.Null((await SavedReturnAsync()).SapCreditMemoDocEntry);
    }

    [Fact]
    public async Task Losing_the_race_for_the_credit_memo_gets_a_business_message()
    {
        await SeedAsync();
        await AddCreditMemoAsync();

        var error = await Assert.ThrowsAsync<ApplicationException>(
            () => new RacingLink(_db, Finder(), rivalTakesIt: true).ExecuteAsync(_load.Key, _return.Key, 1, "tester"));

        Assert.Equal(ShipmentLoadsLinkSapCreditMemoService.LinkedElsewhereMessage, error.Message);
        Assert.Null((await SavedReturnAsync()).SapCreditMemoDocEntry);
    }

    [Fact]
    public async Task Unexpected_write_failure_does_not_leak_the_database_message()
    {
        await SeedAsync();
        await AddCreditMemoAsync();

        var error = await Assert.ThrowsAsync<ApplicationException>(
            () => new RacingLink(_db, Finder(), rivalTakesIt: false).ExecuteAsync(_load.Key, _return.Key, 1, "tester"));

        Assert.DoesNotContain("SALES_INVOICES", error.Message);
        Assert.Contains("Nenhuma alteração foi gravada", error.Message);
    }

    // ---------- desvincular ----------

    private async Task SeedLinkedAsync()
    {
        await SeedAsync();
        await AddCreditMemoAsync();
        await Link().ExecuteAsync(_load.Key, _return.Key, 1, "tester");
        _db.Context.ChangeTracker.Clear();
    }

    [Fact]
    public async Task Unlink_clears_the_credit_memo_number_series_and_key_and_keeps_the_lines()
    {
        await SeedLinkedAsync();

        await Unlink().ExecuteAsync(_load.Key, _return.Key, "tester");

        var saved = await SavedReturnAsync();
        Assert.Null(saved.SapCreditMemoDocEntry);
        Assert.Null(saved.SapCreditMemoDocNum);
        Assert.Null(saved.TaxDocumentNumber);
        Assert.Null(saved.TaxDocumentSeries);
        Assert.Null(saved.ChaveNFe);
        Assert.False(saved.IsSapLinked);
        Assert.Equal(InvoiceStatus.Confirmed, saved.InvoiceStatus);
        var line = Assert.Single(saved.Items);
        Assert.Equal("1201", line.Cfop);
        Assert.Equal(71, line.UsageCode);
    }

    [Fact]
    public async Task Unlinked_credit_memo_can_be_linked_again()
    {
        await SeedLinkedAsync();
        await Unlink().ExecuteAsync(_load.Key, _return.Key, "tester");
        _db.Context.ChangeTracker.Clear();

        await Link().ExecuteAsync(_load.Key, _return.Key, 1, "tester");

        Assert.Equal(1, (await SavedReturnAsync()).SapCreditMemoDocEntry);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("NF de devolução cancelada no SAP.", "NF de devolução cancelada no SAP.")]
    public async Task Unlink_is_registered_in_the_change_log_with_the_reason(string? reason, string? expectedNewValue)
    {
        await SeedLinkedAsync();

        await Unlink().ExecuteAsync(_load.Key, _return.Key, "sap-sync", reason);

        var log = await _db.Context.SalesInvoicesChangeLogs.AsNoTracking()
            .OrderByDescending(x => x.Key).FirstAsync(x => x.ChangedBy == "sap-sync");
        Assert.Equal(ContractChangeLogFields.SapReturnInvoice, log.Field);
        Assert.Equal("NF 000004512/001 (doc. SAP 2001)", log.OldValue);
        Assert.Equal(expectedNewValue, log.NewValue);
    }

    [Fact]
    public async Task Unlink_without_the_load_key_serves_the_sync()
    {
        await SeedLinkedAsync();

        await Unlink().ExecuteAsync(null, _return.Key, "sap-sync", "NF de devolução cancelada no SAP.");

        Assert.Null((await SavedReturnAsync()).SapCreditMemoDocEntry);
    }

    [Fact]
    public async Task Unlink_is_refused_when_nothing_is_linked()
    {
        await SeedAsync();

        var error = await Assert.ThrowsAsync<ApplicationException>(
            () => Unlink().ExecuteAsync(_load.Key, _return.Key, "tester"));

        Assert.Equal(ShipmentLoadsUnlinkSapCreditMemoService.NotLinkedMessage, error.Message);
    }

    [Fact]
    public async Task Unlink_of_a_document_of_another_load_is_not_found()
    {
        await SeedLinkedAsync();

        await Assert.ThrowsAsync<NotFoundException>(
            () => Unlink().ExecuteAsync(Guid.NewGuid(), _return.Key, "tester"));

        Assert.Equal(1, (await SavedReturnAsync()).SapCreditMemoDocEntry);
    }

    [Fact]
    public async Task Unlink_is_refused_outside_sap_mode()
    {
        await SeedLinkedAsync();

        var error = await Assert.ThrowsAsync<ApplicationException>(
            () => Unlink("STANDALONE").ExecuteAsync(_load.Key, _return.Key, "tester"));

        Assert.Equal(ShipmentLoadsLinkSapCreditMemoService.SapOnlyMessage, error.Message);
    }
}
