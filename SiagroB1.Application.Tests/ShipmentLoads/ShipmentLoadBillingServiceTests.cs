using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services;
using SiagroB1.Application.Services.SalesContracts;
using SiagroB1.Application.Services.SalesInvoices;
using SiagroB1.Application.Services.SalesShipmentReleases;
using SiagroB1.Application.Services.ShipmentBilling;
using SiagroB1.Application.Services.ShipmentLoads;
using SiagroB1.Application.Tests.SalesContracts;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.ShipmentLoads;

/// <summary>
/// Faturamento POR CARGA, com emissão parcial. A carga é o pivô: a nota aponta a carga e
/// nunca escreve <c>SalesTransactions</c>.
/// </summary>
public class ShipmentLoadBillingServiceTests
{
    private const string CardCode = "C0001";

    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private static IBusinessPartnerService Partners() =>
        new FakeBusinessPartnerService(
            names: new Dictionary<string, string> { [CardCode] = "CLIENTE TESTE" },
            states: new Dictionary<string, string> { [CardCode] = "RS" });

    private ShipmentBillingCreateSalesInvoiceService Service()
    {
        var usages = new UsageService(_db, NullLogger<UsageService>.Instance);
        var partners = Partners();

        return new ShipmentBillingCreateSalesInvoiceService(
            _db,
            new SalesInvoicesCreateService(
                _db,
                partners,
                new FakeItemService(new Dictionary<string, string> { ["SOJA"] = "SOJA EM GRAOS" }),
                new FakeDocNumberSequenceService(),
                new SalesInvoicesUsageGuardService(usages),
                new SalesInvoicesCfopResolveService(_db, usages, partners),
                NullLogger<SalesInvoicesCreateService>.Instance),
            new ShipmentBillingTransactionGuardService(_db.Context),
            new SalesShipmentReleaseMovementGuardService(_db.Context),
            new SalesShipmentReleasesRecalculateShippedService(_db.Context),
            new SalesContractsAllocationCreateService(
                _db, new SalesContractsFixedVolumeService(_db.Context)),
            new ShipmentLoadsBillingGuardService(_db.Context),
            new ShipmentLoadsRecalculateInvoicedService(_db.Context),
            new ShipmentLoadsMovementLogService(_db.Context),
            NullLogger<ShipmentBillingCreateSalesInvoiceService>.Instance);
    }

    private async Task<(ShipmentLoad Load, SalesContract Contract, SalesShipmentRelease Release,
        StorageTransaction Shipment)> SeedAsync()
    {
        var load = new ShipmentLoad
        {
            Key = Guid.NewGuid(),
            Code = "CG000007",
            BranchCode = "01",
            ItemCode = "SOJA",
            ItemName = "SOJA EM GRAOS",
            UnitOfMeasureCode = "KG",
            TruckCode = "ABC1D23",
            TotalQuantity = 90_000m,
        };

        var contract = SalesContractsAllocationTestSupport.NewContract(totalVolume: 1_000_000m);
        var release = SalesContractsAllocationTestSupport.NewRelease(contract.Key, released: 1_000_000m);

        var shipment = new StorageTransaction
        {
            Key = Guid.NewGuid(),
            Code = "R1",
            CardCode = CardCode,
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            WarehouseCode = "ARM01",
            BranchCode = "01",
            TruckCode = "ABC1D23",
            GrossWeight = 90_000m,
            NetWeight = 90_000m,
            TransactionType = StorageTransactionType.SalesShipment,
            TransactionStatus = StorageTransactionsStatus.Confirmed,
            ShipmentLoadKey = load.Key,
        };

        _db.Context.ShipmentLoads.Add(load);
        _db.Context.SalesContracts.Add(contract);
        _db.Context.SalesShipmentReleases.Add(release);
        _db.Context.StorageTransactions.Add(shipment);
        _db.Context.Branchs.Add(new Branch { Code = "01", BranchName = "MATRIZ", StateCode = "RS" });
        await _db.SaveChangesAsync();

        return (load, contract, release, shipment);
    }

    private static SalesInvoice InvoiceFor(
        ShipmentLoad load, SalesContract contract, SalesShipmentRelease release, decimal quantity)
    {
        var invoice = new SalesInvoice
        {
            Key = Guid.NewGuid(),
            CardCode = CardCode,
            BranchCode = "01",
            ShipmentLoadKey = load.Key,
            InvoiceStatus = InvoiceStatus.Pending,
            InvoiceType = SalesInvoiceType.Normal,
        };

        invoice.AddItem(new SalesInvoiceItem
        {
            Key = Guid.NewGuid(),
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            Quantity = quantity,
            UnitPrice = 90m,
            SalesContractKey = contract.Key,
            SalesShipmentReleaseKey = release.Key,
        });

        return invoice;
    }

    [Fact]
    public async Task Billing_part_of_the_load_leaves_it_partially_invoiced_with_balance()
    {
        var (load, contract, release, _) = await SeedAsync();

        await Service().ExecuteAsync(InvoiceFor(load, contract, release, 40_000m), "tester");

        var saved = await _db.Context.ShipmentLoads.AsNoTracking().SingleAsync();
        Assert.Equal(ShipmentLoadStatus.PartiallyInvoiced, saved.Status);
        Assert.Equal(40_000m, saved.InvoicedQuantity);
        Assert.Equal(50_000m, saved.AvailableQuantity);

        // O ledger do contrato e o eixo da liberação acompanham.
        var allocation = Assert.Single(await _db.Context.SalesContractsAllocations.AsNoTracking().ToListAsync());
        Assert.Equal(SalesContractAllocationOrigin.Billing, allocation.Origin);
        Assert.Equal(40_000m, allocation.Volume);

        var savedRelease = await _db.Context.SalesShipmentReleases.AsNoTracking().SingleAsync();
        Assert.Equal(40_000m, savedRelease.ShippedQuantity);
    }

    [Fact]
    public async Task The_load_invoice_never_writes_shipment_links()
    {
        var (load, contract, release, shipment) = await SeedAsync();

        await Service().ExecuteAsync(InvoiceFor(load, contract, release, 40_000m), "tester");

        var savedShipment = await _db.Context.StorageTransactions.AsNoTracking().SingleAsync();
        // Com N notas por carga, SalesInvoiceKey não tem dono único — preenchê-lo com "uma
        // das notas" é mentira estrutural que o guard de duplicidade leria como "já faturado".
        Assert.Null(savedShipment.SalesInvoiceKey);
        Assert.Null(savedShipment.SalesShipmentReleaseKey);
        Assert.Equal(shipment.Key, savedShipment.Key);

        var savedInvoice = await _db.Context.SalesInvoices
            .AsNoTracking().Include(x => x.SalesTransactions).SingleAsync();
        Assert.Empty(savedInvoice.SalesTransactions);
        Assert.Equal(load.Key, savedInvoice.ShipmentLoadKey);
        Assert.Equal(InvoiceStatus.Confirmed, savedInvoice.InvoiceStatus);
    }

    [Fact]
    public async Task Billing_the_rest_closes_the_load_and_the_shipments()
    {
        var (load, contract, release, shipment) = await SeedAsync();

        await Service().ExecuteAsync(InvoiceFor(load, contract, release, 40_000m), "tester");
        await Service().ExecuteAsync(InvoiceFor(load, contract, release, 50_000m), "tester");

        var saved = await _db.Context.ShipmentLoads.AsNoTracking().SingleAsync();
        Assert.Equal(ShipmentLoadStatus.Invoiced, saved.Status);
        Assert.Equal(decimal.Zero, saved.AvailableQuantity);

        var savedShipment = await _db.Context.StorageTransactions
            .AsNoTracking().SingleAsync(x => x.Key == shipment.Key);
        Assert.Equal(StorageTransactionsStatus.Invoiced, savedShipment.TransactionStatus);
    }

    [Fact]
    public async Task Billing_beyond_the_balance_is_refused_without_writing_anything()
    {
        var (load, contract, release, _) = await SeedAsync();

        await Service().ExecuteAsync(InvoiceFor(load, contract, release, 90_000m), "tester");

        await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync(InvoiceFor(load, contract, release, 1_000m), "tester"));

        // Uma tentativa recusada não pode deixar rastro: o guard roda antes de qualquer escrita.
        Assert.Equal(1, await _db.Context.SalesInvoices.CountAsync());
        Assert.Equal(90_000m, (await _db.Context.ShipmentLoads.AsNoTracking().SingleAsync()).InvoicedQuantity);
    }

    [Fact]
    public async Task Billing_records_a_Billed_movement_with_the_balance_after()
    {
        var (load, contract, release, _) = await SeedAsync();

        await Service().ExecuteAsync(InvoiceFor(load, contract, release, 40_000m), "tester");

        var movement = await _db.Context.ShipmentLoadMovements.AsNoTracking().SingleAsync();
        Assert.Equal(ShipmentLoadMovementType.Billed, movement.MovementType);
        // Quantidade ASSINADA: consumo é negativo.
        Assert.Equal(-40_000m, movement.Quantity);
        Assert.Equal(50_000m, movement.BalanceAfter);
        Assert.NotNull(movement.SalesInvoiceKey);
    }

    [Fact]
    public async Task A_cancelled_load_cannot_be_billed()
    {
        var (load, contract, release, _) = await SeedAsync();

        var tracked = await _db.Context.ShipmentLoads.SingleAsync();
        tracked.Status = ShipmentLoadStatus.Cancelled;
        await _db.SaveChangesAsync();

        await Assert.ThrowsAsync<ApplicationException>(
            () => Service().ExecuteAsync(InvoiceFor(load, contract, release, 1_000m), "tester"));

        Assert.Equal(0, await _db.Context.SalesInvoices.CountAsync());
    }

    [Fact]
    public async Task A_contract_with_no_balance_is_still_billed()
    {
        // O faturamento NÃO valida saldo de contrato — decisão preservada de propósito
        // (ver o <remarks> de ShipmentBillingCreateSalesInvoiceService). O caminhão já saiu.
        var (load, contract, release, _) = await SeedAsync();

        var trackedContract = await _db.Context.SalesContracts.SingleAsync();
        trackedContract.TotalVolume = 10_000m;
        trackedContract.AllocatedVolume = 10_000m;
        await _db.SaveChangesAsync();

        await Service().ExecuteAsync(InvoiceFor(load, contract, release, 40_000m), "tester");

        var saved = await _db.Context.SalesContracts.AsNoTracking().SingleAsync();
        Assert.True(saved.AvaiableVolume < decimal.Zero);
        Assert.Equal(1, await _db.Context.SalesInvoices.CountAsync());
    }
}
