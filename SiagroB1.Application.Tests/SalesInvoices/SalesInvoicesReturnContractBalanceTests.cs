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
using static SiagroB1.Application.Tests.SalesContracts.SalesContractsAllocationTestSupport;

namespace SiagroB1.Application.Tests.SalesInvoices;

/// <summary>
/// O que o RETORNO devolve ao saldo do contrato de venda, ponta a ponta: da tela de retorno até
/// o <c>AllocatedVolume</c> do contrato e o <c>ShippedQuantity</c> da liberação.
/// </summary>
/// <remarks>
/// As suítes existentes cobrem os dois lados separados — <c>SalesInvoicesReturnServiceTests</c>
/// exercita o retorno sem contrato nenhum, e <c>SalesContractsAllocationReturnProportionalTests</c>
/// chama o ledger sem passar pela confirmação. O relato do usuário ("mesmo devolvendo menos, o
/// contrato recebe o total da nota de origem") cai exatamente na emenda entre os dois.
/// </remarks>
public class SalesInvoicesReturnContractBalanceTests
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

    private StorageTransactionsCreateService StorageCreate() =>
        new(_db,
            new FakeDocNumberSequenceService(),
            Partners(),
            Items(),
            Warehouses(),
            new ShipmentReleasesRecalculateShippedService(_db.Context),
            new ShipmentReleaseMovementGuardService(_db.Context),
            NullLogger<StorageTransactionsCreateService>.Instance);

    private StorageTransactionsConfirmedService StorageConfirm() =>
        new(_db,
            new FakeStringLocalizer<Resource>(),
            new ShipmentReleasesRecalculateShippedService(_db.Context),
            new ShipmentReleaseMovementGuardService(_db.Context),
            NullLogger<StorageTransactionsConfirmedService>.Instance);

    private SalesInvoicesReturnService Service() =>
        new(_db,
            CreateService(),
            ConfirmService(),
            StorageCreate(),
            StorageConfirm(),
            Warehouses(),
            NullLogger<SalesInvoicesReturnService>.Instance);

    private SalesContract _contract = null!;
    private SalesShipmentRelease _release = null!;
    private SalesInvoice _invoice = null!;
    private SalesInvoiceItem _item = null!;
    private StorageTransaction _r1 = null!;
    private StorageTransaction _r2 = null!;

    /// <summary>
    /// O estado que o faturamento de expedição deixa: nota legada confirmada com dois romaneios
    /// de 20.000, item de 40.000 amarrado a contrato e liberação, e a linha POSITIVA do ledger
    /// que consome o contrato.
    /// </summary>
    private async Task SeedAsync(SalesInvoiceDeliveryStatus deliveryStatus =
        SalesInvoiceDeliveryStatus.Open)
    {
        _contract = NewContract(totalVolume: 100_000m);
        _release = NewRelease(_contract.Key, released: 60_000m, shipped: 40_000m);
        _contract.AllocatedVolume = 40_000m;

        _invoice = new SalesInvoice
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
            DeliveryStatus = deliveryStatus,
            GrossWeight = 40_000m,
            NetWeight = 40_000m,
        };

        _item = new SalesInvoiceItem
        {
            Key = Guid.NewGuid(),
            ItemCode = "SOJA",
            ItemName = "SOJA EM GRAOS",
            UnitOfMeasureCode = "KG",
            Quantity = 40_000m,
            UnitPrice = 90m,
            SalesContractKey = _contract.Key,
            SalesShipmentReleaseKey = _release.Key,
            DeliveryStatus = deliveryStatus,
            DeliveredQuantity = deliveryStatus == SalesInvoiceDeliveryStatus.Closed ? 40_000m : 0m,
        };

        _invoice.AddItem(_item);

        _r1 = Shipment(_invoice, "R1");
        _r2 = Shipment(_invoice, "R2");

        _db.Context.AddRange(_contract, _release);
        _db.Context.SalesInvoices.Add(_invoice);
        _db.Context.StorageTransactions.AddRange(_r1, _r2);
        _db.Context.Branchs.Add(
            new Branch { Code = "01", BranchName = "MATRIZ", StateCode = "RS" });

        await _db.SaveChangesAsync();

        // A linha do faturamento: 40.000 consumidos do contrato, na liberação, dona da diferença.
        var billing = NewAllocation(_contract.Key, _item.Key!.Value, 40_000m, _release.Key);
        billing.OwnsDeliveryDifference = true;
        _db.Context.SalesContractsAllocations.Add(billing);

        await _db.SaveChangesAsync();
    }

    private static StorageTransaction Shipment(SalesInvoice invoice, string code) =>
        new()
        {
            Key = Guid.NewGuid(),
            Code = code,
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

    private SalesInvoiceReturnRequest WarehouseRequest(
        IReadOnlyList<(StorageTransaction Shipment, decimal? Quantity)> lines) =>
        new(_invoice.Key,
            [.. lines.Select(l => new SalesInvoiceReturnShipment(l.Shipment.Key, l.Quantity))],
            RefusalDestination.Warehouse,
            DestinationWarehouse,
            "Cliente recebeu parte da carga e recusou o restante");

    /// <summary>
    /// Devolver 12.000 de uma carreta de 20.000 tem de devolver 12.000 ao contrato — e não os
    /// 40.000 da nota de origem inteira.
    /// </summary>
    [Fact]
    public async Task A_partial_quantity_return_gives_back_only_the_returned_quantity()
    {
        await SeedAsync();

        await Service().ExecuteAsync(WarehouseRequest([(_r1, 12_000m)]), "tester");

        var contract = await ContractAsync(_db, _contract.Key);

        Assert.Equal(28_000m, contract.AllocatedVolume);
        Assert.Equal(72_000m, contract.AvaiableVolume);
    }

    /// <summary>A liberação segue o mesmo número do contrato: os dois eixos nunca divergem.</summary>
    [Fact]
    public async Task A_partial_quantity_return_gives_back_only_the_returned_quantity_to_the_release()
    {
        await SeedAsync();

        await Service().ExecuteAsync(WarehouseRequest([(_r1, 12_000m)]), "tester");

        Assert.Equal(28_000m, (await ReleaseAsync(_db, _release.Key)).ShippedQuantity);
    }

    /// <summary>
    /// Devolver a carreta INTEIRA (sem quantidade informada) devolve os 20.000 dela, e não a
    /// nota toda.
    /// </summary>
    [Fact]
    public async Task A_whole_shipment_return_gives_back_only_that_shipment()
    {
        await SeedAsync();

        await Service().ExecuteAsync(WarehouseRequest([(_r1, null)]), "tester");

        Assert.Equal(20_000m, (await ContractAsync(_db, _contract.Key)).AllocatedVolume);
    }

    /// <summary>Devolvida a nota inteira, o contrato volta ao consumo zero.</summary>
    [Fact]
    public async Task A_total_return_gives_the_whole_document_back()
    {
        await SeedAsync();

        await Service().ExecuteAsync(WarehouseRequest([(_r1, null), (_r2, null)]), "tester");

        Assert.Equal(0m, (await ContractAsync(_db, _contract.Key)).AllocatedVolume);
    }

    /// <summary>
    /// Duas devoluções parciais somam o que voltou — a segunda não pode reabrir o efeito da
    /// primeira nem contá-la de novo.
    /// </summary>
    [Fact]
    public async Task Sequential_partial_returns_add_up_on_the_contract()
    {
        await SeedAsync();

        await Service().ExecuteAsync(WarehouseRequest([(_r1, 12_000m)]), "tester");
        Assert.Equal(28_000m, (await ContractAsync(_db, _contract.Key)).AllocatedVolume);

        await Service().ExecuteAsync(WarehouseRequest([(_r1, 5_000m)]), "tester");
        Assert.Equal(23_000m, (await ContractAsync(_db, _contract.Key)).AllocatedVolume);
    }

    /// <summary>
    /// O ledger guarda a quantidade DEVOLVIDA na linha negativa — é ela que o recálculo derivado
    /// da soma lê, e onde um "total da origem" apareceria.
    /// </summary>
    [Fact]
    public async Task The_ledger_records_the_returned_quantity_as_a_negative_line()
    {
        await SeedAsync();

        var returnInvoice = await Service().ExecuteAsync(
            WarehouseRequest([(_r1, 12_000m)]), "tester");

        var returnItemKeys = await _db.Context.SalesInvoicesItems
            .AsNoTracking()
            .Where(x => x.SalesInvoiceKey == returnInvoice.Key)
            .Select(x => x.Key!.Value)
            .ToListAsync();

        var rows = await _db.Context.SalesContractsAllocations
            .AsNoTracking()
            .Where(x => returnItemKeys.Contains(x.SalesInvoiceItemKey))
            .ToListAsync();

        Assert.Equal(-12_000m, Assert.Single(rows).Volume);
    }
}
