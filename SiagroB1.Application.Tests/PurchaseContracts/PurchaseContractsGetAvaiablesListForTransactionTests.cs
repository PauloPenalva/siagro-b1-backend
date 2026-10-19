using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.PurchaseContracts;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.PurchaseContracts;

/// <summary>
/// Lista de contratos do diálogo de alocação a partir do romaneio: a perna de compra de uma
/// Expedição só vai para o contrato da liberação (decisão 7 do spec), e o DTO avisa o diálogo
/// para não tratar o armazém da liberação como divergência.
/// </summary>
public class PurchaseContractsGetAvaiablesListForTransactionTests
{
    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private PurchaseContractsGetService Service() =>
        new(_db, NullLogger<PurchaseContractsGetService>.Instance);

    private PurchaseContract AddContract(string code, string cardCode, string location = "01")
    {
        var contract = new PurchaseContract
        {
            Key = Guid.NewGuid(), Code = code, CardCode = cardCode, CardName = $"NOME {cardCode}",
            ItemCode = "SOJA", UnitOfMeasureCode = "KG", HarvestSeasonCode = "24/25",
            DeliveryLocationCode = location, DeliveryLocationName = $"ARMAZEM {location}",
            TotalVolume = 1000m, AllocatedVolume = 0m, Status = ContractStatus.Approved,
        };
        _db.Context.PurchaseContracts.Add(contract);
        return contract;
    }

    private StorageTransaction AddTransaction(string warehouseCode = "01")
    {
        var st = new StorageTransaction
        {
            Key = Guid.NewGuid(), Code = "RC-1", CardCode = "F0001", ItemCode = "SOJA", UnitOfMeasureCode = "KG",
            WarehouseCode = warehouseCode, TransactionType = StorageTransactionType.Purchase,
            TransactionStatus = StorageTransactionsStatus.Confirmed, NetWeight = 500m, AvaiableVolumeToAllocate = 500m,
        };
        _db.Context.StorageTransactions.Add(st);
        return st;
    }

    [Fact]
    public async Task RegularTransaction_ListsContractsOfAnySupplier_SameSupplierFirst()
    {
        AddContract("PC-001", "F0002");
        AddContract("PC-002", "F0001");
        var st = AddTransaction();
        await _db.Context.SaveChangesAsync();

        var list = await Service().GetAvaiablesPurchaseContractsForTransactionAsync(st.Key);

        Assert.Equal(["PC-002", "PC-001"], list.Select(x => x.Code));
        Assert.All(list, x => Assert.False(x.IsShippingReleaseContract));
    }

    [Fact]
    public async Task ShippingLeg_ListsOnlyTheReleaseContract_Flagged()
    {
        AddContract("PC-001", "F0001");
        var releaseContract = AddContract("PC-002", "F0001", location: "05");
        var st = AddTransaction(warehouseCode: "09");
        var release = new ShipmentRelease
        {
            Key = Guid.NewGuid(), PurchaseContractKey = releaseContract.Key, DeliveryLocationCode = "09",
            ReleasedQuantity = 1_000m, Status = ReleaseStatus.Actived,
        };
        _db.Context.ShipmentReleases.Add(release);
        st.ShipmentReleaseKey = release.Key;
        _db.Context.ShippingTransactions.Add(new ShippingTransaction
        {
            Key = Guid.NewGuid(), PurchaseStorageTransactionKey = st.Key,
        });
        await _db.Context.SaveChangesAsync();

        var dto = Assert.Single(await Service().GetAvaiablesPurchaseContractsForTransactionAsync(st.Key));

        Assert.Equal("PC-002", dto.Code);
        Assert.True(dto.IsShippingReleaseContract);
    }

    [Fact]
    public async Task UnknownTransaction_Throws()
    {
        await Assert.ThrowsAsync<NotFoundException>(() =>
            Service().GetAvaiablesPurchaseContractsForTransactionAsync(Guid.NewGuid()));
    }
}
