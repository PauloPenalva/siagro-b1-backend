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
            Warehouses(),
            NullLogger<SalesInvoicesReturnService>.Instance);

    private SalesInvoicesReverseConfirmService ReverseService() =>
        new(_db,
            new SalesContractsAllocationDeleteForInvoiceService(_db),
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
}
