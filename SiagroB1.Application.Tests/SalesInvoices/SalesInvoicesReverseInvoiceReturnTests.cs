using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services;
using SiagroB1.Application.Services.SalesContracts;
using SiagroB1.Application.Services.SalesInvoices;
using SiagroB1.Application.Services.SalesShipmentReleases;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Application.Services.ShipmentReleases;
using SiagroB1.Application.Services.StorageTransactions;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.SalesInvoices;

/// <summary>
/// Estorno da confirmação de uma devolução nascida do RETORNO de um documento de saída legado.
/// </summary>
/// <remarks>
/// O estorno tem de desfazer exatamente o que a confirmação aplicou, e nos dois destinos isso é
/// coisa diferente: no "segue viagem" é o romaneio solto que precisa voltar à nota; no "armazém"
/// é o crédito de estoque que não pode ficar de pé sozinho.
/// <para>
/// O cenário é montado pelo caminho REAL (<see cref="SalesInvoicesReturnService"/>) e não à mão:
/// metade das armadilhas está justamente em como o retorno deixa as coisas.
/// </para>
/// </remarks>
public class SalesInvoicesReverseInvoiceReturnTests
{
    private const string CardCode = "C0001";
    private const string OriginWarehouse = "ARM01";
    private const string DestinationWarehouse = "ARM99";

    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private static IBusinessPartnerService Partners() =>
        new FakeBusinessPartnerService(
            names: new Dictionary<string, string> { [CardCode] = "CLIENTE TESTE" },
            states: new Dictionary<string, string> { [CardCode] = "RS" });

    private static FakeItemService Items() =>
        new(new Dictionary<string, string> { ["SOJA"] = "SOJA EM GRAOS" });

    private static FakeWarehouseService Warehouses() =>
        new(new Dictionary<string, string>
        {
            [OriginWarehouse] = "ARMAZEM CEAGESP",
            [DestinationWarehouse] = "ARMAZEM RETAGUARDA",
        });

    private SalesInvoicesCreateService CreateService()
    {
        var usages = new UsageService(_db, NullLogger<UsageService>.Instance);
        var partners = Partners();

        return new SalesInvoicesCreateService(
            _db,
            partners,
            Items(),
            new FakeDocNumberSequenceService(),
            new SalesInvoicesUsageGuardService(usages),
            new SalesInvoicesCfopResolveService(_db, usages, partners),
            NullLogger<SalesInvoicesCreateService>.Instance);
    }

    private SalesInvoicesConfirmService ConfirmService() =>
        new(_db,
            new SalesShipmentReleasesRecalculateShippedService(_db.Context),
            new SalesContractsAllocationCreateService(
                _db, new SalesContractsFixedVolumeService(_db.Context)),
            new SalesContractsAllocationCreateForReturnService(
                _db, new SalesContractsFixedVolumeService(_db.Context)),
            new SalesInvoicesUsageGuardService(
                new UsageService(_db, NullLogger<UsageService>.Instance)),
            new SalesContractsAllocationCreateForFiscalAdjustmentService(
                _db, new SalesContractsFixedVolumeService(_db.Context)),
            new ShipmentLoadsBalanceHookService(
                _db.Context, new ShipmentLoadsMovementLogService(_db.Context)),
            new FakeStringLocalizer<Resource>());

    private SalesInvoicesReturnService ReturnService() =>
        new(_db,
            CreateService(),
            ConfirmService(),
            new StorageTransactionsCreateService(
                _db,
                new FakeDocNumberSequenceService(),
                Partners(),
                Items(),
                Warehouses(),
                new ShipmentReleasesRecalculateShippedService(_db.Context),
                new ShipmentReleaseMovementGuardService(_db.Context),
                NullLogger<StorageTransactionsCreateService>.Instance),
            new StorageTransactionsConfirmedService(
                _db,
                new FakeStringLocalizer<Resource>(),
                new ShipmentReleasesRecalculateShippedService(_db.Context),
                new ShipmentReleaseMovementGuardService(_db.Context),
                NullLogger<StorageTransactionsConfirmedService>.Instance),
            new ShipmentReleasesFromReturnService(_db.Context),
            Warehouses(),
            NullLogger<SalesInvoicesReturnService>.Instance);

    private SalesInvoicesReverseConfirmService ReverseService() =>
        new(_db,
            new SalesContractsAllocationDeleteForInvoiceService(_db),
            new ShipmentReleasesRecalculateShippedService(_db.Context),
            new ShipmentLoadsBalanceHookService(
                _db.Context, new ShipmentLoadsMovementLogService(_db.Context)),
            new FakeStringLocalizer<Resource>());

    private async Task<(SalesInvoice Invoice, StorageTransaction R1)> SeedAsync()
    {
        var invoice = new SalesInvoice
        {
            Key = Guid.NewGuid(),
            CardCode = CardCode,
            CardName = "CLIENTE TESTE",
            BranchCode = "01",
            InvoiceNumber = "000001",
            TaxDocumentNumber = "12345",
            TaxDocumentSeries = "1",
            InvoiceStatus = InvoiceStatus.Confirmed,
            InvoiceType = SalesInvoiceType.Normal,
            GrossWeight = 20_000m,
            NetWeight = 20_000m,
        };

        invoice.AddItem(new SalesInvoiceItem
        {
            Key = Guid.NewGuid(),
            ItemCode = "SOJA",
            ItemName = "SOJA EM GRAOS",
            UnitOfMeasureCode = "KG",
            Quantity = 20_000m,
            UnitPrice = 90m,
        });

        var r1 = new StorageTransaction
        {
            Key = Guid.NewGuid(),
            Code = "R1",
            CardCode = CardCode,
            ItemCode = "SOJA",
            ItemName = "SOJA EM GRAOS",
            UnitOfMeasureCode = "KG",
            WarehouseCode = OriginWarehouse,
            BranchCode = "01",
            TruckCode = "ABC1D23",
            GrossWeight = 20_000m,
            NetWeight = 20_000m,
            InvoiceQty = 20_000m,
            IsInvoiced = true,
            TransactionType = StorageTransactionType.SalesShipment,
            TransactionStatus = StorageTransactionsStatus.Invoiced,
            SalesInvoiceKey = invoice.Key,
        };

        _db.Context.SalesInvoices.Add(invoice);
        _db.Context.StorageTransactions.Add(r1);
        _db.Context.Branchs.Add(new Branch { Code = "01", BranchName = "MATRIZ", StateCode = "RS" });
        await _db.SaveChangesAsync();

        return (invoice, r1);
    }

    private Task<StorageTransaction> ShipmentAsync(Guid key) =>
        _db.Context.StorageTransactions.AsNoTracking().SingleAsync(x => x.Key == key);

    /// <summary>
    /// Estornar um retorno "segue viagem" traz o romaneio de volta para a nota. Sem isso ele fica
    /// solto e disponível, enquanto a nota que o faturava volta a valer — o mesmo volume em dois
    /// lugares.
    /// </summary>
    [Fact]
    public async Task Reversing_a_rebilling_return_reattaches_the_shipment_to_the_origin()
    {
        var (invoice, r1) = await SeedAsync();

        var returnInvoice = await ReturnService().ExecuteAsync(
            new SalesInvoiceReturnRequest(
                invoice.Key,
                [new SalesInvoiceReturnShipment(r1.Key, null)],
                RefusalDestination.Rebilling, null, "Recusa"),
            "tester");

        await ReverseService().ExecuteAsync(returnInvoice.Key, "tester");

        var restored = await ShipmentAsync(r1.Key);

        Assert.Equal(StorageTransactionsStatus.Invoiced, restored.TransactionStatus);
        Assert.Equal(invoice.Key, restored.SalesInvoiceKey);
        Assert.True(restored.IsInvoiced);
        Assert.Null(restored.ReturnInvoiceKey);
    }

    /// <summary>
    /// Estornar um retorno "para armazém" cancela o romaneio de devolução. Deixá-lo de pé manteria
    /// o grão creditado no armazém enquanto a devolução volta a Pendente e a nota volta a valer.
    /// </summary>
    [Fact]
    public async Task Reversing_a_warehouse_return_cancels_the_return_shipment()
    {
        var (invoice, r1) = await SeedAsync();

        var returnInvoice = await ReturnService().ExecuteAsync(
            new SalesInvoiceReturnRequest(
                invoice.Key,
                [new SalesInvoiceReturnShipment(r1.Key, null)],
                RefusalDestination.Warehouse, DestinationWarehouse, "Recusa"),
            "tester");

        await ReverseService().ExecuteAsync(returnInvoice.Key, "tester");

        var entry = await _db.Context.StorageTransactions
            .AsNoTracking()
            .SingleAsync(x => x.TransactionType == StorageTransactionType.SalesShipmentReturn);

        Assert.Equal(StorageTransactionsStatus.Cancelled, entry.TransactionStatus);
    }

    /// <summary>
    /// Depois de "segue viagem" o romaneio volta ao pool e pode ser FATURADO em outra nota — que é
    /// o objetivo do destino. O <c>ReturnInvoiceKey</c> da devolução antiga continua nele, e é por
    /// ele que o estorno procura: sem uma guarda, estornar aquela devolução sequestraria o
    /// romaneio da nota nova, deixando o mesmo volume faturado em duas.
    /// </summary>
    [Fact]
    public async Task Reversing_a_return_does_not_hijack_a_shipment_already_rebilled()
    {
        var (invoice, r1) = await SeedAsync();

        var returnInvoice = await ReturnService().ExecuteAsync(
            new SalesInvoiceReturnRequest(
                invoice.Key,
                [new SalesInvoiceReturnShipment(r1.Key, null)],
                RefusalDestination.Rebilling, null, "Recusa"),
            "tester");

        // O romaneio, agora solto, é faturado em outra nota.
        var rebilled = new SalesInvoice
        {
            Key = Guid.NewGuid(),
            CardCode = CardCode,
            BranchCode = "01",
            InvoiceNumber = "000009",
            InvoiceStatus = InvoiceStatus.Confirmed,
            InvoiceType = SalesInvoiceType.Normal,
        };

        _db.Context.SalesInvoices.Add(rebilled);

        var shipment = await _db.Context.StorageTransactions.SingleAsync(x => x.Key == r1.Key);
        shipment.SalesInvoiceKey = rebilled.Key;
        shipment.TransactionStatus = StorageTransactionsStatus.Invoiced;
        shipment.IsInvoiced = true;
        await _db.SaveChangesAsync();

        await ReverseService().ExecuteAsync(returnInvoice.Key, "tester");

        var afterReverse = await ShipmentAsync(r1.Key);

        Assert.Equal(rebilled.Key, afterReverse.SalesInvoiceKey);
    }

    /// <summary>
    /// A devolução estornada volta a Pendente e continua existindo — por isso a origem segue
    /// retornada. Quem desfaz o estado da origem é o cancelamento/exclusão da devolução, não o
    /// estorno: cada efeito é desfeito no nível em que foi aplicado.
    /// </summary>
    [Fact]
    public async Task Reversing_a_return_keeps_the_origin_returned()
    {
        var (invoice, r1) = await SeedAsync();

        var returnInvoice = await ReturnService().ExecuteAsync(
            new SalesInvoiceReturnRequest(
                invoice.Key,
                [new SalesInvoiceReturnShipment(r1.Key, null)],
                RefusalDestination.Warehouse, DestinationWarehouse, "Recusa"),
            "tester");

        await ReverseService().ExecuteAsync(returnInvoice.Key, "tester");

        var origin = await _db.Context.SalesInvoices.AsNoTracking().SingleAsync(x => x.Key == invoice.Key);
        var reversed = await _db.Context.SalesInvoices.AsNoTracking().SingleAsync(x => x.Key == returnInvoice.Key);

        Assert.Equal(InvoiceStatus.Returned, origin.InvoiceStatus);
        Assert.Equal(InvoiceStatus.Pending, reversed.InvoiceStatus);
    }

    // ---------- desfazer também desfaz a liberação que a devolução emitiu ----------

    /// <summary>
    /// Pendura o romaneio numa liberação de COMPRA para que a devolução consiga rastrear o
    /// contrato e emitir a sua liberação — sem isso não há o que estornar.
    /// </summary>
    private async Task SeedPurchaseOriginAsync(StorageTransaction shipment)
    {
        var contract = new PurchaseContract
        {
            Key = Guid.NewGuid(),
            Code = "PC-ORIG",
            CardCode = "F0001",
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            HarvestSeasonCode = "2026",
            DeliveryLocationCode = "ARM01",
            BranchCode = "01",
            Status = ContractStatus.Approved,
            TotalVolume = 1_000_000m,
        };
        var originRelease = new ShipmentRelease
        {
            Key = Guid.NewGuid(),
            PurchaseContractKey = contract.Key,
            DeliveryLocationCode = "ARM01",
            ReleasedQuantity = 1_000_000m,
            Status = ReleaseStatus.Actived,
        };
        _db.Context.PurchaseContracts.Add(contract);
        _db.Context.ShipmentReleases.Add(originRelease);

        var tracked = await _db.Context.StorageTransactions.SingleAsync(x => x.Key == shipment.Key);
        tracked.ShipmentReleaseKey = originRelease.Key;

        await _db.SaveChangesAsync();
    }

    private async Task<SalesInvoice> ReturnToWarehouseAsync(SalesInvoice invoice, StorageTransaction r1) =>
        await ReturnService().ExecuteAsync(
            new SalesInvoiceReturnRequest(
                invoice.Key,
                [new SalesInvoiceReturnShipment(r1.Key, null)],
                RefusalDestination.Warehouse, DestinationWarehouse, "Recusa"),
            "tester");

    [Fact]
    public async Task Reversing_a_warehouse_return_cancels_the_release_it_emitted()
    {
        var (invoice, r1) = await SeedAsync();
        await SeedPurchaseOriginAsync(r1);

        var returnInvoice = await ReturnToWarehouseAsync(invoice, r1);

        await ReverseService().ExecuteAsync(returnInvoice.Key, "tester");

        var release = await _db.Context.ShipmentReleases
            .AsNoTracking()
            .SingleAsync(x => x.Origin == ReleaseOrigin.SalesReturn);

        Assert.Equal(ReleaseStatus.Cancelled, release.Status);
        Assert.Contains("Estorno da devolução ao armazém", release.CancellationReason);
        Assert.Equal("tester", release.CanceledBy);
    }

    /// <summary>
    /// Cancelar a liberação de devolução não pode CREDITAR o contrato: ela nunca o debitou.
    /// </summary>
    [Fact]
    public async Task Cancelling_a_return_release_does_not_credit_the_purchase_contract()
    {
        var (invoice, r1) = await SeedAsync();
        await SeedPurchaseOriginAsync(r1);

        var returnInvoice = await ReturnToWarehouseAsync(invoice, r1);
        await ReverseService().ExecuteAsync(returnInvoice.Key, "tester");

        var contract = await _db.Context.PurchaseContracts
            .AsNoTracking()
            .Include(x => x.ShipmentReleases)
            .SingleAsync();

        Assert.Equal(1_000_000m, contract.TotalShipmentReleases); // só a liberação original
        Assert.Equal(0m, contract.ShipmentReleases
            .Single(x => x.Origin == ReleaseOrigin.SalesReturn).ReturnedToContractQuantity);
    }

    /// <summary>
    /// ⚠️ O guard: se a mercadoria devolvida já saiu de novo, desfazer a devolução deixaria uma
    /// liberação cancelada com embarque em cima e o armazém com saldo negativo. Recusa por
    /// inteiro, sem deixar efeito no banco.
    /// </summary>
    [Fact]
    public async Task Reversing_a_warehouse_return_is_refused_when_the_goods_were_shipped_again()
    {
        var (invoice, r1) = await SeedAsync();
        await SeedPurchaseOriginAsync(r1);

        var returnInvoice = await ReturnToWarehouseAsync(invoice, r1);

        var release = await _db.Context.ShipmentReleases
            .SingleAsync(x => x.Origin == ReleaseOrigin.SalesReturn);

        // Simula o reembarque: a perna de saída consome a liberação.
        _db.Context.StorageTransactions.Add(new StorageTransaction
        {
            Key = Guid.NewGuid(),
            Code = "REEMB",
            CardCode = "C0001",
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            WarehouseCode = DestinationWarehouse,
            TransactionType = StorageTransactionType.SalesShipment,
            TransactionStatus = StorageTransactionsStatus.Confirmed,
            NetWeight = 5_000m,
            ShipmentReleaseKey = release.Key,
        });
        await _db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<ApplicationException>(
            () => ReverseService().ExecuteAsync(returnInvoice.Key, "tester"));

        Assert.Contains("já foi embarcada de novo", ex.Message);

        // Nada gravado: nem a entrada cancelada, nem a liberação.
        var entry = await _db.Context.StorageTransactions
            .AsNoTracking()
            .SingleAsync(x => x.TransactionType == StorageTransactionType.SalesShipmentReturn);
        Assert.Equal(StorageTransactionsStatus.Confirmed, entry.TransactionStatus);

        var reloaded = await _db.Context.ShipmentReleases
            .AsNoTracking().SingleAsync(x => x.Key == release.Key);
        Assert.Equal(ReleaseStatus.Actived, reloaded.Status);
    }
}
