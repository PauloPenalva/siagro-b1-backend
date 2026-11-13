using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.ShipmentLoads;

/// <summary>
/// Sementes da transferência entre armazéns de terceiros (GAC-1176): a carga em transbordo com a
/// entrada registrada, do jeito que <c>ShipmentLoadsTransshipmentRegisterEntryService</c> a deixa.
/// </summary>
internal static class ShipmentLoadsTransferTestData
{
    public const string OriginWarehouse = "ARM01";
    public const string DestinationWarehouse = "ARM99";

    public static ShipmentLoad Load(IUnitOfWork db, decimal totalQuantity = 30_000m, string code = "CG000001")
    {
        var load = new ShipmentLoad
        {
            Key = Guid.NewGuid(),
            Code = code,
            BranchCode = "01",
            ItemCode = "SOJA",
            ItemName = "SOJA EM GRAOS",
            UnitOfMeasureCode = "KG",
            TruckCode = "ABC1D23",
            WarehouseCode = OriginWarehouse,
            Status = ShipmentLoadStatus.InTransshipment,
            TotalQuantity = totalQuantity,
            TransshippedQuantity = totalQuantity,
        };
        db.Context.ShipmentLoads.Add(load);

        // O romaneio de saída da ORIGEM: é ele que o recálculo projeta (Confirmed ↔ Invoiced).
        db.Context.StorageTransactions.Add(new StorageTransaction
        {
            Key = Guid.NewGuid(),
            Code = $"R-ORIGEM-{code}",
            CardCode = "C0001",
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            WarehouseCode = OriginWarehouse,
            BranchCode = "01",
            GrossWeight = totalQuantity,
            NetWeight = totalQuantity,
            TransactionType = StorageTransactionType.SalesShipment,
            TransactionStatus = StorageTransactionsStatus.Confirmed,
            ShipmentLoadKey = load.Key,
        });

        return load;
    }

    /// <summary>Transbordo ainda SEM entrada registrada.</summary>
    public static async Task<ShipmentLoadTransshipment> TransshipmentAsync(
        IUnitOfWork db, ShipmentLoad load, decimal outgoing = 30_000m, int sequence = 1,
        TransshipmentType type = TransshipmentType.Transshipment)
    {
        var transshipment = new ShipmentLoadTransshipment
        {
            ShipmentLoadKey = load.Key,
            Sequence = sequence,
            Type = type,
            WarehouseCode = DestinationWarehouse,
            WarehouseName = "ARMAZEM DESTINO",
            OutgoingQuantity = outgoing,
        };
        db.Context.ShipmentLoadsTransshipments.Add(transshipment);
        await db.Context.SaveChangesAsync();
        return transshipment;
    }

    /// <summary>
    /// Entrada registrada em armazém de TERCEIRO: romaneio 15 confirmado + liberação de origem
    /// Transshipment gerada por ele.
    /// </summary>
    public static async Task<(StorageTransaction Entry, ShipmentRelease Release)> RegisterThirdPartyEntryAsync(
        IUnitOfWork db, ShipmentLoadTransshipment transshipment, decimal entryQuantity = 29_800m,
        decimal shippedQuantity = 0m)
    {
        var entry = new StorageTransaction
        {
            Key = Guid.NewGuid(),
            Code = $"R0015-{transshipment.Sequence}",
            CardCode = "C0001",
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            WarehouseCode = DestinationWarehouse,
            BranchCode = "01",
            TruckCode = "ABC1D23",
            GrossWeight = entryQuantity,
            NetWeight = entryQuantity,
            TransactionType = StorageTransactionType.TransshipmentReceipt,
            TransactionStatus = StorageTransactionsStatus.Confirmed,
            ShipmentLoadTransshipmentKey = transshipment.Key,
        };
        db.Context.StorageTransactions.Add(entry);

        transshipment.EntryQuantity = entryQuantity;
        transshipment.EntryStorageTransactionKey = entry.Key;

        var contract = new PurchaseContract
        {
            Key = Guid.NewGuid(),
            Code = $"PC-{transshipment.Sequence}",
            CardCode = "F0001",
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            HarvestSeasonCode = "2026",
            DeliveryLocationCode = OriginWarehouse,
            BranchCode = "01",
            Status = ContractStatus.Approved,
            TotalVolume = 1_000_000m,
        };
        db.Context.PurchaseContracts.Add(contract);

        var release = new ShipmentRelease
        {
            Key = Guid.NewGuid(),
            PurchaseContractKey = contract.Key,
            DeliveryLocationCode = DestinationWarehouse,
            ReleasedQuantity = entryQuantity,
            ShippedQuantity = shippedQuantity,
            Status = ReleaseStatus.Actived,
            Origin = ReleaseOrigin.Transshipment,
            GeneratedByStorageTransactionKey = entry.Key,
        };
        db.Context.ShipmentReleases.Add(release);

        await db.Context.SaveChangesAsync();
        return (entry, release);
    }

    /// <summary>Entrada em armazém PRÓPRIO: o Receipt (0) da Entrada em Armazenagem, só vinculado.</summary>
    public static async Task<StorageTransaction> RegisterOwnWarehouseEntryAsync(
        IUnitOfWork db, ShipmentLoadTransshipment transshipment, decimal entryQuantity = 30_000m)
    {
        var receipt = new StorageTransaction
        {
            Key = Guid.NewGuid(),
            Code = "E0001",
            CardCode = "F0001",
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            WarehouseCode = DestinationWarehouse,
            BranchCode = "01",
            GrossWeight = entryQuantity,
            NetWeight = entryQuantity,
            TransactionType = StorageTransactionType.Receipt,
            TransactionStatus = StorageTransactionsStatus.Confirmed,
            ShipmentLoadTransshipmentKey = transshipment.Key,
        };
        db.Context.StorageTransactions.Add(receipt);

        transshipment.EntryQuantity = entryQuantity;
        transshipment.EntryStorageTransactionKey = receipt.Key;

        await db.Context.SaveChangesAsync();
        return receipt;
    }

    /// <summary>Expedição de venda vinculada como SAÍDA do transbordo — o que o fecha pelo caminho de sempre.</summary>
    public static async Task LinkExitAsync(IUnitOfWork db, ShipmentLoad load, ShipmentLoadTransshipment transshipment)
    {
        db.Context.StorageTransactions.Add(new StorageTransaction
        {
            Key = Guid.NewGuid(),
            Code = "R-RECARGA",
            CardCode = "C0001",
            ItemCode = "SOJA",
            UnitOfMeasureCode = "KG",
            WarehouseCode = DestinationWarehouse,
            BranchCode = "01",
            GrossWeight = 1_000m,
            NetWeight = 1_000m,
            TransactionType = StorageTransactionType.SalesShipment,
            TransactionStatus = StorageTransactionsStatus.Confirmed,
            ShipmentLoadKey = load.Key,
            ShipmentLoadTransshipmentKey = transshipment.Key,
        });
        await db.Context.SaveChangesAsync();
    }
}
