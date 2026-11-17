using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services;
using SiagroB1.Application.Services.SalesContracts;
using SiagroB1.Application.Services.SalesInvoices;
using SiagroB1.Application.Services.SalesShipmentReleases;
using SiagroB1.Application.Services.SAP;
using SiagroB1.Application.Services.ShipmentReleases;
using SiagroB1.Application.Services.ShipmentBilling;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Application.Tests.Accounting;
using SiagroB1.Application.Tests.SalesContracts;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Entities.SAP;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Infra;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Tests.SalesInvoices;

/// <summary>
/// Regras do documento de saída vinculado a uma NF do SAP (GAC-1226): número/série/chave e linhas comerciais
/// travados, mas o vínculo errado pode ser desfeito (estornar, excluir, cancelar), e reconfirmar revalida a NF no SAP.
/// </summary>
public class SalesInvoiceSapLinkedRulesTests
{
    private const string CardCode = "C0001";
    private const string Carrier = "F004702";
    private const string Item = "SOJA";
    private const decimal Quantity = 90_000m;
    private const string SapBranch = "3";

    private const string TaxDocumentMessage = "Número, série e chave deste documento vêm da nota fiscal do SAP.";
    private const string LinesMessage =
        "Documento vinculado à nota fiscal do SAP: quantidade, preço e item não podem ser alterados.";
    private const string CanceledMessage = "A NF do SAP vinculada foi cancelada. Exclua este documento.";
    private const string NotFoundMessage = "A NF do SAP vinculada não foi encontrada. Exclua este documento.";

    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();
    private readonly SapErpDbContext _sap = new(
        new DbContextOptionsBuilder<SapErpDbContext>().UseInMemoryDatabase($"sap-{Guid.NewGuid()}").Options);

    private ShipmentLoad _load = null!;
    private SalesShipmentRelease _release = null!;

    private sealed class NoFiscal : ISapFiscalDocumentSource
    {
        public bool IsAvailable => false;

        public Task<IReadOnlyDictionary<int, SapFiscalDocumentInfo>> GetForInvoicesAsync(
            IReadOnlyCollection<SapInvoiceRef> invoices, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyDictionary<int, SapFiscalDocumentInfo>>(new Dictionary<int, SapFiscalDocumentInfo>());
    }

    // ---------- semeadura e serviços ----------

    private SapLinkableInvoicesService Linkable() =>
        new(_db.Context, _sap, new NoFiscal(), new ShipmentLoadsBillingGuardService(_db.Context));

    private ShipmentLoadsLinkSapInvoiceService LinkService()
    {
        var usages = new Services.UsageService(_db, NullLogger<Services.UsageService>.Instance);
        IBusinessPartnerService partners = new FakeBusinessPartnerService(
            names: new Dictionary<string, string> { [CardCode] = "CLIENTE TESTE" },
            states: new Dictionary<string, string> { [CardCode] = "RS" });
        var config = AccountingTestData.Config("SAPB1");

        var billing = new ShipmentBillingCreateSalesInvoiceService(
            _db,
            new SalesInvoicesCreateService(
                _db, partners,
                new FakeItemService(new Dictionary<string, string> { [Item] = "SOJA EM GRAOS" }),
                new FakeDocNumberSequenceService(),
                new SalesInvoicesUsageGuardService(usages),
                new SalesInvoicesCfopResolveService(_db, usages, partners),
                TaxTestServices.InactiveApply(_db),
                TaxTestServices.FiscalComplement(_db, "SAPB1"), NullLogger<SalesInvoicesCreateService>.Instance),
            new ShipmentBillingTransactionGuardService(_db.Context),
            new SalesShipmentReleaseMovementGuardService(_db.Context),
            new SalesShipmentReleasesRecalculateShippedService(_db.Context),
            new SalesContractsAllocationCreateService(_db, new SalesContractsFixedVolumeService(_db.Context)),
            new ShipmentLoadsBillingGuardService(_db.Context),
            new ShipmentLoadsRecalculateInvoicedService(_db, new ShipmentLoadsChangeLogService(_db.Context)),
            new ShipmentLoadsMovementLogService(_db.Context),
            NullLogger<ShipmentBillingCreateSalesInvoiceService>.Instance,
            configuration: config);

        return new ShipmentLoadsLinkSapInvoiceService(
            _db, Linkable(), new SalesInvoiceTaxDocumentDuplicateGuard(_db.Context), billing, config);
    }

    private ShipmentLoadsBalanceHookService Hook() =>
        new(_db.Context, new ShipmentLoadsMovementLogService(_db.Context));

    private SalesInvoicesConfirmService Confirm(bool withSap = true) =>
        new(_db,
            new SalesShipmentReleasesRecalculateShippedService(_db.Context),
            new SalesContractsAllocationCreateService(_db, new SalesContractsFixedVolumeService(_db.Context)),
            new SalesContractsAllocationCreateForReturnService(_db, new SalesContractsFixedVolumeService(_db.Context)),
            new SalesInvoicesUsageGuardService(new Services.UsageService(_db, NullLogger<Services.UsageService>.Instance)),
            new SalesContractsAllocationCreateForFiscalAdjustmentService(_db, new SalesContractsFixedVolumeService(_db.Context)),
            Hook(),
            new ShipmentLoadsClosureHookService(_db.Context, new ShipmentLoadsChangeLogService(_db.Context)),
            new FakeStringLocalizer<Resource>(),
            sapErp: withSap ? _sap : null);

    /// <summary>Confirmação cuja leitura do SAP falha, como o banco fora do ar.</summary>
    private sealed class SapDownConfirm(
        UnitOfWork db, ShipmentLoadsBalanceHookService hook, SapErpDbContext sap, Exception failure)
        : SalesInvoicesConfirmService(db,
            new SalesShipmentReleasesRecalculateShippedService(db.Context),
            new SalesContractsAllocationCreateService(db, new SalesContractsFixedVolumeService(db.Context)),
            new SalesContractsAllocationCreateForReturnService(db, new SalesContractsFixedVolumeService(db.Context)),
            new SalesInvoicesUsageGuardService(new Services.UsageService(db, NullLogger<Services.UsageService>.Instance)),
            new SalesContractsAllocationCreateForFiscalAdjustmentService(db, new SalesContractsFixedVolumeService(db.Context)),
            hook,
            new ShipmentLoadsClosureHookService(db.Context, new ShipmentLoadsChangeLogService(db.Context)),
            new FakeStringLocalizer<Resource>(),
            sapErp: sap)
    {
        protected override Task<(bool Exists, string? Canceled)> FindSapInvoiceAsync(SapErpDbContext sap, int docEntry) =>
            throw failure;
    }

    private sealed class FakeDbException(string message) : System.Data.Common.DbException(message);

    private SalesInvoicesReverseConfirmService ReverseConfirm() =>
        new(_db,
            new SalesContractsAllocationDeleteForInvoiceService(_db),
            new ShipmentReleasesRecalculateShippedService(_db.Context),
            Hook(),
            new ShipmentLoadsClosureHookService(_db.Context, new ShipmentLoadsChangeLogService(_db.Context)),
            new FakeStringLocalizer<Resource>());

    private SalesInvoicesCancelService Cancel() =>
        new(_db,
            new SalesShipmentReleasesRecalculateShippedService(_db.Context),
            new SalesContractsAllocationDeleteForInvoiceService(_db),
            Hook(),
            NullLogger<SalesInvoicesCancelService>.Instance);

    private SalesInvoicesDeleteService Delete() =>
        new(_db, Hook(), NullLogger<SalesInvoicesDeleteService>.Instance);

    private SalesInvoicesSetDocumentNumberService SetNumber() =>
        new(_db, new SalesInvoicesChangeLogService(_db.Context));

    private SalesInvoicesItemsUpdateService ItemUpdate() =>
        new(_db, new FakeItemService(new Dictionary<string, string> { [Item] = "SOJA EM GRAOS", ["MILHO"] = "MILHO" }),
            new ShipmentLoadsClosureHookService(_db.Context, new ShipmentLoadsChangeLogService(_db.Context)),
            TaxTestServices.InactiveApply(_db), TaxTestServices.InactiveFiscalComplement(_db),
            PurchaseDischargeTestServices.Sync(_db), NullLogger<SalesInvoicesUpdateService>.Instance);

    private SalesInvoicesItemsCreateService ItemCreate() =>
        new(_db, new FakeItemService(new Dictionary<string, string> { [Item] = "SOJA EM GRAOS" }),
            TaxTestServices.InactiveApply(_db), TaxTestServices.InactiveFiscalComplement(_db),
            NullLogger<SalesInvoicesItemsCreateService>.Instance);

    private SalesInvoicesItemsDeleteService ItemDelete() =>
        new(_db, TaxTestServices.InactiveApply(_db), NullLogger<SalesInvoicesItemsDeleteService>.Instance);

    private async Task SeedAsync()
    {
        _load = new ShipmentLoad
        {
            Key = Guid.NewGuid(), Code = "CG000010", BranchCode = SapBranch, ItemCode = Item, ItemName = "SOJA EM GRAOS",
            UnitOfMeasureCode = "KG", TruckCode = "ABC1D23", TruckDriverCode = "MOT01", CarrierCardCode = Carrier,
            CarrierName = "TRANSPORTES YOKOTOBI LTDA", TotalQuantity = Quantity, FreightPrice = 12.5m,
            Status = ShipmentLoadStatus.Open,
        };
        var contract = SalesContractsAllocationTestSupport.NewContract(
            totalVolume: 1_000_000m, cardCode: CardCode, itemCode: Item);
        _release = SalesContractsAllocationTestSupport.NewRelease(contract.Key, released: 1_000_000m, status: ReleaseStatus.Actived);
        _release.DeliveryLocationCode = "LOCAL77";

        _db.Context.ShipmentLoads.Add(_load);
        _db.Context.SalesContracts.Add(contract);
        _db.Context.SalesShipmentReleases.Add(_release);
        _db.Context.Branchs.Add(new Branch { Code = SapBranch, BranchName = "FILIAL 3", StateCode = "RS" });
        await _db.SaveChangesAsync();
    }

    private async Task AddSapInvoiceAsync(int docEntry = 1)
    {
        _sap.SapInvoices.Add(new SapInvoice
        {
            DocEntry = docEntry, DocNum = docEntry + 1000, DocDate = new DateTime(2026, 11, 15), CardCode = CardCode,
            CardName = "CLIENTE TESTE", BPLId = 3, Canceled = "N", Serial = 198105, SeriesStr = "1", Model = "39",
        });
        _sap.SapInvoiceLines.Add(new SapInvoiceLine
        {
            DocEntry = docEntry, LineNum = 0, ItemCode = Item, Quantity = Quantity, Price = 2.5m,
            LineTotal = Quantity * 2.5m, CfopCode = "5101",
        });
        _sap.SapInvoiceTransports.Add(new SapInvoiceTransport { DocEntry = docEntry, Carrier = Carrier, Incoterms = "1" });
        await _sap.SaveChangesAsync();
    }

    /// <summary>Documento vinculado e Confirmado, como o vínculo o entrega.</summary>
    private async Task<SalesInvoice> LinkedAsync()
    {
        await SeedAsync();
        await AddSapInvoiceAsync();
        var invoice = (await LinkService().ExecuteAsync(_load.Key, [1], _release.Key, "tester")).Single();
        _db.Context.ChangeTracker.Clear();
        return invoice;
    }

    private async Task<SalesInvoiceItem> LineAsync() =>
        await _db.Context.SalesInvoicesItems.SingleAsync();

    // ---------- regra 1: NF manual recusada ----------

    [Fact]
    public async Task Manual_tax_document_is_refused_on_a_linked_document()
    {
        var invoice = await LinkedAsync();

        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            SetNumber().ExecuteAsync(invoice.Key, "123", "1", null, "tester"));

        Assert.Equal(TaxDocumentMessage, ex.Message);
    }

    [Fact]
    public async Task Without_tax_document_flag_is_refused_on_a_linked_document()
    {
        var invoice = await LinkedAsync();

        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            SetNumber().ExecuteAsync(invoice.Key, null, null, null, "tester", withoutTaxDocument: true));

        Assert.Equal(TaxDocumentMessage, ex.Message);
    }

    [Fact]
    public async Task Clearing_the_tax_document_is_refused_on_a_linked_document()
    {
        var invoice = await LinkedAsync();

        var ex = await Assert.ThrowsAsync<DefaultException>(() =>
            SetNumber().ExecuteAsync(invoice.Key, "", "", "", "tester"));

        Assert.Equal(TaxDocumentMessage, ex.Message);
        var saved = await _db.Context.SalesInvoices.AsNoTracking().SingleAsync();
        Assert.Equal("000198105", saved.TaxDocumentNumber);
    }

    [Fact]
    public async Task Manual_tax_document_still_works_on_an_unlinked_document()
    {
        var invoice = await LinkedAsync();
        var stored = await _db.Context.SalesInvoices.SingleAsync();
        stored.SapDocEntry = null;
        stored.SapDocNum = null;
        await _db.SaveChangesAsync();

        await SetNumber().ExecuteAsync(invoice.Key, "777", "2", null, "tester");

        var saved = await _db.Context.SalesInvoices.AsNoTracking().SingleAsync();
        Assert.Equal("777", saved.TaxDocumentNumber);
    }

    // ---------- regra 2: quantidade, preço e item travados ----------

    [Fact]
    public async Task Quantity_change_is_refused_on_a_linked_document()
    {
        await LinkedAsync();
        var line = await LineAsync();
        line.Quantity = 80_000m;

        var ex = await Assert.ThrowsAsync<DefaultException>(() => ItemUpdate().ExecuteAsync(line.Key!.Value, line, "tester"));

        Assert.Equal(LinesMessage, ex.Message);
    }

    [Fact]
    public async Task Price_change_is_refused_on_a_linked_document()
    {
        await LinkedAsync();
        var line = await LineAsync();
        line.UnitPrice = 9m;

        var ex = await Assert.ThrowsAsync<DefaultException>(() => ItemUpdate().ExecuteAsync(line.Key!.Value, line, "tester"));

        Assert.Equal(LinesMessage, ex.Message);
    }

    [Fact]
    public async Task Item_change_is_refused_on_a_linked_document()
    {
        await LinkedAsync();
        var line = await LineAsync();
        line.ItemCode = "MILHO";

        var ex = await Assert.ThrowsAsync<DefaultException>(() => ItemUpdate().ExecuteAsync(line.Key!.Value, line, "tester"));

        Assert.Equal(LinesMessage, ex.Message);
    }

    [Fact]
    public async Task Adding_a_line_is_refused_on_a_linked_document()
    {
        var invoice = await LinkedAsync();

        var ex = await Assert.ThrowsAsync<DefaultException>(() => ItemCreate().ExecuteAsync(
            new SalesInvoiceItem
            {
                Key = Guid.NewGuid(), SalesInvoiceKey = invoice.Key, ItemCode = Item, UnitOfMeasureCode = "KG", Quantity = 1m,
            }, "tester"));

        Assert.Equal(LinesMessage, ex.Message);
    }

    [Fact]
    public async Task Removing_a_line_is_refused_on_a_linked_document()
    {
        await LinkedAsync();
        var line = await LineAsync();

        var ex = await Assert.ThrowsAsync<DefaultException>(() => ItemDelete().ExecuteAsync(line.Key!.Value));

        Assert.Equal(LinesMessage, ex.Message);
    }

    [Fact]
    public async Task Delivery_reconciliation_stays_editable_on_a_linked_document()
    {
        await LinkedAsync();
        var line = await LineAsync();
        line.DeliveredQuantity = 89_000m;
        line.QuantityLoss = 1_000m;

        await ItemUpdate().ExecuteAsync(line.Key!.Value, line, "tester");

        var saved = await _db.Context.SalesInvoicesItems.AsNoTracking().SingleAsync();
        Assert.Equal(89_000m, saved.DeliveredQuantity);
        Assert.Equal(1_000m, saved.QuantityLoss);
        Assert.Equal(Quantity, saved.Quantity);
    }

    [Fact]
    public async Task Line_edits_still_work_on_an_unlinked_document()
    {
        var invoice = await LinkedAsync();
        var stored = await _db.Context.SalesInvoices.SingleAsync();
        stored.SapDocEntry = null;
        stored.SapDocNum = null;
        await _db.SaveChangesAsync();
        var line = await LineAsync();
        line.Quantity = 80_000m;

        await ItemUpdate().ExecuteAsync(line.Key!.Value, line, "tester");

        Assert.Equal(80_000m, (await _db.Context.SalesInvoicesItems.AsNoTracking().SingleAsync()).Quantity);
        Assert.Equal(invoice.Key, (await _db.Context.SalesInvoices.AsNoTracking().SingleAsync()).Key);
    }

    // ---------- regra 3: desfazer o vínculo errado (caracterização: já funcionava) ----------

    [Fact]
    public async Task Reverse_confirmation_goes_back_to_pending_and_keeps_the_sap_link()
    {
        var invoice = await LinkedAsync();

        await ReverseConfirm().ExecuteAsync(invoice.Key, "tester");

        var saved = await _db.Context.SalesInvoices.AsNoTracking().SingleAsync();
        Assert.Equal(InvoiceStatus.Pending, saved.InvoiceStatus);
        Assert.Equal(1, saved.SapDocEntry);
        Assert.Equal(1001, saved.SapDocNum);
        // A NF segue reservada: não aparece para vincular de novo.
        Assert.Empty(await Linkable().ListAsync(_load.Key, new DateTime(2026, 1, 1), new DateTime(2026, 12, 31), CancellationToken.None));
    }

    [Fact]
    public async Task Pending_linked_document_can_be_deleted_and_the_nf_is_linkable_again()
    {
        var invoice = await LinkedAsync();
        await ReverseConfirm().ExecuteAsync(invoice.Key, "tester");
        _db.Context.ChangeTracker.Clear();

        var deleted = await Delete().ExecuteAsync(invoice.Key, "tester");

        Assert.True(deleted);
        Assert.Empty(await _db.Context.SalesInvoices.AsNoTracking().ToListAsync());
        var load = await _db.Context.ShipmentLoads.AsNoTracking().SingleAsync();
        Assert.Equal(0m, load.InvoicedQuantity);
        var listed = await Linkable().ListAsync(_load.Key, new DateTime(2026, 1, 1), new DateTime(2026, 12, 31), CancellationToken.None);
        Assert.Contains(listed, x => x.DocEntry == 1);
    }

    [Fact]
    public async Task Cancelling_a_linked_document_releases_the_nf()
    {
        var invoice = await LinkedAsync();

        await Cancel().ExecuteAsync(invoice.Key, "tester");

        var saved = await _db.Context.SalesInvoices.AsNoTracking().SingleAsync();
        Assert.Equal(InvoiceStatus.Cancelled, saved.InvoiceStatus);
        var listed = await Linkable().ListAsync(_load.Key, new DateTime(2026, 1, 1), new DateTime(2026, 12, 31), CancellationToken.None);
        Assert.Contains(listed, x => x.DocEntry == 1);
    }

    [Fact]
    public async Task Header_patch_does_not_wipe_the_sap_link()
    {
        var invoice = await LinkedAsync();
        await ReverseConfirm().ExecuteAsync(invoice.Key, "tester");
        _db.Context.ChangeTracker.Clear();

        var body = await _db.Context.SalesInvoices.AsNoTracking().SingleAsync();
        body.SapDocEntry = null;
        body.SapDocNum = null;
        body.Comments = "alterado";
        var update = new SalesInvoicesUpdateService(_db,
            new FakeBusinessPartnerService(names: new() { [CardCode] = "CLIENTE TESTE" }),
            TaxTestServices.InactiveApply(_db), TaxTestServices.InactiveFiscalComplement(_db),
            NullLogger<SalesInvoicesUpdateService>.Instance);

        await update.ExecuteAsync(invoice.Key, body, "tester");

        var saved = await _db.Context.SalesInvoices.AsNoTracking().SingleAsync();
        Assert.Equal(1, saved.SapDocEntry);
        Assert.Equal(1001, saved.SapDocNum);
        Assert.Equal("alterado", saved.Comments);
    }

    // ---------- regra 4: reconfirmar revalida a NF no SAP ----------

    [Fact]
    public async Task Reconfirming_refuses_when_the_sap_invoice_was_canceled()
    {
        var invoice = await LinkedAsync();
        await ReverseConfirm().ExecuteAsync(invoice.Key, "tester");
        _db.Context.ChangeTracker.Clear();
        (await _sap.SapInvoices.SingleAsync()).Canceled = "Y";
        await _sap.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<DefaultException>(() => Confirm().ExecuteAsync(invoice.Key, "tester"));

        Assert.Equal(CanceledMessage, ex.Message);
        Assert.Equal(InvoiceStatus.Pending, (await _db.Context.SalesInvoices.AsNoTracking().SingleAsync()).InvoiceStatus);
    }

    [Fact]
    public async Task Reconfirming_refuses_when_the_sap_invoice_no_longer_exists()
    {
        var invoice = await LinkedAsync();
        await ReverseConfirm().ExecuteAsync(invoice.Key, "tester");
        _db.Context.ChangeTracker.Clear();
        _sap.SapInvoices.RemoveRange(await _sap.SapInvoices.ToListAsync());
        await _sap.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<DefaultException>(() => Confirm().ExecuteAsync(invoice.Key, "tester"));

        Assert.Equal(NotFoundMessage, ex.Message);
    }

    [Fact]
    public async Task Reconfirming_succeeds_while_the_sap_invoice_is_still_valid()
    {
        var invoice = await LinkedAsync();
        await ReverseConfirm().ExecuteAsync(invoice.Key, "tester");
        _db.Context.ChangeTracker.Clear();

        await Confirm().ExecuteAsync(invoice.Key, "tester");

        var saved = await _db.Context.SalesInvoices.AsNoTracking().SingleAsync();
        Assert.Equal(InvoiceStatus.Confirmed, saved.InvoiceStatus);
        Assert.Equal(1, saved.SapDocEntry);
    }

    [Fact]
    public async Task Reconfirming_skips_the_check_without_the_sap_context()
    {
        var invoice = await LinkedAsync();
        await ReverseConfirm().ExecuteAsync(invoice.Key, "tester");
        _db.Context.ChangeTracker.Clear();
        (await _sap.SapInvoices.SingleAsync()).Canceled = "Y";
        await _sap.SaveChangesAsync();

        await Confirm(withSap: false).ExecuteAsync(invoice.Key, "tester");

        Assert.Equal(InvoiceStatus.Confirmed, (await _db.Context.SalesInvoices.AsNoTracking().SingleAsync()).InvoiceStatus);
    }

    [Fact]
    public async Task Reconfirming_an_unlinked_document_ignores_the_sap()
    {
        var invoice = await LinkedAsync();
        await ReverseConfirm().ExecuteAsync(invoice.Key, "tester");
        _db.Context.ChangeTracker.Clear();
        var stored = await _db.Context.SalesInvoices.SingleAsync();
        stored.SapDocEntry = null;
        stored.SapDocNum = null;
        await _db.SaveChangesAsync();
        _sap.SapInvoices.RemoveRange(await _sap.SapInvoices.ToListAsync());
        await _sap.SaveChangesAsync();
        _db.Context.ChangeTracker.Clear();

        await Confirm().ExecuteAsync(invoice.Key, "tester");

        Assert.Equal(InvoiceStatus.Confirmed, (await _db.Context.SalesInvoices.AsNoTracking().SingleAsync()).InvoiceStatus);
    }

    [Fact]
    public async Task Reconfirming_refuses_when_the_sap_database_fails()
    {
        var invoice = await LinkedAsync();
        await ReverseConfirm().ExecuteAsync(invoice.Key, "tester");
        _db.Context.ChangeTracker.Clear();

        foreach (Exception failure in new Exception[] { new FakeDbException("fora do ar"), new TimeoutException() })
        {
            var ex = await Assert.ThrowsAsync<DefaultException>(() =>
                new SapDownConfirm(_db, Hook(), _sap, failure).ExecuteAsync(invoice.Key, "tester"));

            Assert.Equal("Não foi possível consultar as notas fiscais no SAP. Tente novamente em instantes.", ex.Message);
            Assert.Equal(InvoiceStatus.Pending,
                (await _db.Context.SalesInvoices.AsNoTracking().SingleAsync()).InvoiceStatus);
        }
    }

    [Fact]
    public async Task Reconfirming_does_not_swallow_unexpected_sap_failures()
    {
        var invoice = await LinkedAsync();
        await ReverseConfirm().ExecuteAsync(invoice.Key, "tester");
        _db.Context.ChangeTracker.Clear();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new SapDownConfirm(_db, Hook(), _sap, new InvalidOperationException("bug")).ExecuteAsync(invoice.Key, "tester"));
    }
}
