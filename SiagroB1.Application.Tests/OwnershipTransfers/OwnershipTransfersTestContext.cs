using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.OwnershipTransfers;
using SiagroB1.Application.Services.PurchaseContracts;
using SiagroB1.Application.Services.ShipmentReleases;
using SiagroB1.Application.Services.StorageTransactions;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.OwnershipTransfers;

/// <summary>
/// Monta os serviços de transferência de titularidade sobre um banco InMemory.
/// Compartilhado por Confirm/Cancel/ValidateContract para não repetir a árvore
/// de dependências de <see cref="StorageTransactionsCreateService"/>.
/// </summary>
internal sealed class OwnershipTransfersTestContext
{
    public UnitOfWork Db { get; } = TestDb.CreateUnitOfWork();

    /// <summary>
    /// O saldo real do lote vem de SQL (Dapper), que o provider InMemory não traduz.
    /// Aqui interessa a decisão do guard, não a tradução da query — por isso é injetado.
    /// </summary>
    private sealed class FakeBalance(Func<string, decimal> resolve) : IStorageAddressBalanceReader
    {
        public decimal GetBalance(string storageAddressCode) => resolve(storageAddressCode);
    }

    private StorageTransactionsCreateService StorageCreate()
    {
        var recalc = new ShipmentReleasesRecalculateShippedService(Db.Context);

        return new StorageTransactionsCreateService(
            Db,
            new FakeDocNumberSequenceService(),
            new FakeBusinessPartnerService(new()
            {
                ["P0001"] = "Produtor Origem",
                ["E0001"] = "Empresa",
            }),
            new FakeItemService(new() { ["SOJA"] = "SOJA EM GRAOS" }),
            new FakeWarehouseService(new() { ["01"] = "Armazém 01" }),
            recalc,
            new ShipmentReleaseMovementGuardService(Db.Context),
            NullLogger<StorageTransactionsCreateService>.Instance);
    }

    public OwnershipTransfersValidateContractService ValidateContract() =>
        new(Db, new FakeStringLocalizer<Resource>());

    public PurchaseContractsAllocationCreateService AllocationCreate() =>
        new(Db,
            new StorageTransactionsGetService(
                Db, NullLogger<StorageTransactionsGetService>.Instance));

    public OwnershipTransfersConfirmService Confirm(decimal lotBalance = 10_000m) =>
        new(Db,
            StorageCreate(),
            ValidateContract(),
            AllocationCreate(),
            new FakeBalance(_ => lotBalance),
            new FakeStringLocalizer<Resource>(),
            NullLogger<OwnershipTransfersConfirmService>.Instance);

    public static PurchaseContract Contract(
        decimal totalVolume = 10_000m,
        decimal allocatedVolume = 0m,
        ContractStatus status = ContractStatus.Approved,
        string itemCode = "SOJA",
        string uom = "KG") => new()
        {
            Key = Guid.NewGuid(),
            Code = "PC-001",
            CardCode = "F0001",
            ItemCode = itemCode,
            ItemName = "SOJA EM GRAOS",
            UnitOfMeasureCode = uom,
            HarvestSeasonCode = "2026",
            DeliveryLocationCode = "01",
            BranchCode = "01",
            Status = status,
            TotalVolume = totalVolume,
            AllocatedVolume = allocatedVolume,
        };

    public OwnershipTransfersCancelService Cancel(decimal lotBalance = 10_000m)
    {
        var recalc = new ShipmentReleasesRecalculateShippedService(Db.Context);

        return new OwnershipTransfersCancelService(
            Db,
            StorageCreate(),
            new ShipmentReleasesCancelationService(
                Db.Context, recalc,
                NullLogger<ShipmentReleasesCancelationService>.Instance),
            recalc,
            new PurchaseContractsAllocationDeleteService(
                Db, NullLogger<PurchaseContractsAllocationDeleteService>.Instance),
            new StorageTransactionsCancelService(Db, recalc),
            new FakeBalance(_ => lotBalance),
            new FakeStringLocalizer<Resource>(),
            NullLogger<OwnershipTransfersCancelService>.Instance);
    }

    public static StorageAddress Lot(
        string code,
        string cardCode,
        StorageOwnershipType ownershipType,
        string itemCode = "SOJA",
        string uom = "KG",
        StorageAddressStatus status = StorageAddressStatus.Open) => new()
        {
            Code = code,
            Description = $"Lote {code}",
            CardCode = cardCode,
            CardName = cardCode,
            ItemCode = itemCode,
            ItemName = "SOJA EM GRAOS",
            WarehouseCode = "01",
            WarehouseName = "Armazém 01",
            UoM = uom,
            OwnershipType = ownershipType,
            Status = status,
        };

    public static OwnershipTransfer Transfer(
        StorageAddress origin,
        StorageAddress destination,
        decimal quantity = 1000m,
        string itemCode = "SOJA",
        string uom = "KG",
        OwnershipTransferStatus status = OwnershipTransferStatus.Open) => new()
        {
            Key = Guid.NewGuid(),
            TransferCode = "OT-0001",
            Date = DateTime.Now.Date,
            TransferStatus = status,
            ItemCode = itemCode,
            ItemName = "SOJA EM GRAOS",
            UomCode = uom,
            Quantity = quantity,
            StorageAddressOriginCode = origin.Code!,
            StorageAddressDestinationCode = destination.Code!,
        };
}
