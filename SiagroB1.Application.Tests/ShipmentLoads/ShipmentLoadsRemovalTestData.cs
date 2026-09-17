using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.ShipmentLoads;

/// <summary>
/// Fixtures da carga de REMOÇÃO (GAC-1175), compartilhadas pelos testes de vinculação,
/// desvinculação, conclusão e reabertura.
/// </summary>
/// <remarks>
/// A carga de remoção vincula romaneios de <see cref="StorageTransactionType.Receipt"/> — o
/// documento de quem traz mercadoria para dentro de um armazém. Ela usa a MESMA FK da carga de
/// expedição (<c>StorageTransaction.ShipmentLoadKey</c>): o que muda é o tipo aceito.
/// </remarks>
internal static class ShipmentLoadsRemovalTestData
{
    public static ShipmentLoad RemovalLoad(
        IUnitOfWork db,
        ShipmentLoadStatus status = ShipmentLoadStatus.Planned,
        decimal totalQuantity = 0m)
    {
        var load = new ShipmentLoad
        {
            Key = Guid.NewGuid(),
            Code = "CG000001",
            BranchCode = "01",
            ItemCode = "SOJA",
            ItemName = "SOJA EM GRAOS",
            UnitOfMeasureCode = "KG",
            TruckCode = "ABC1D23",
            WarehouseCode = "ARM01",
            LoadType = ShipmentLoadType.Removal,
            Status = status,
            TotalQuantity = totalQuantity,
        };

        db.Context.ShipmentLoads.Add(load);
        return load;
    }

    /// <summary>Romaneio de recebimento — o documento que a carga de remoção vincula.</summary>
    public static StorageTransaction Receipt(
        IUnitOfWork db,
        string code,
        decimal grossWeight = 30_000,
        string truckCode = "ABC1D23",
        string itemCode = "SOJA",
        string branchCode = "01",
        string unitOfMeasureCode = "KG",
        StorageTransactionsStatus status = StorageTransactionsStatus.Confirmed,
        Guid? shipmentLoadKey = null)
    {
        var receipt = new StorageTransaction
        {
            Key = Guid.NewGuid(),
            Code = code,
            CardCode = "F001",
            ItemCode = itemCode,
            ItemName = "SOJA EM GRAOS",
            UnitOfMeasureCode = unitOfMeasureCode,
            WarehouseCode = "ARM01",
            WarehouseName = "ARMAZEM 01",
            BranchCode = branchCode,
            TruckCode = truckCode,
            TruckDriverCode = "M001",
            GrossWeight = grossWeight,
            NetWeight = grossWeight,
            TransactionType = StorageTransactionType.Receipt,
            TransactionStatus = status,
            ShipmentLoadKey = shipmentLoadKey,
        };

        db.Context.StorageTransactions.Add(receipt);
        return receipt;
    }
}
