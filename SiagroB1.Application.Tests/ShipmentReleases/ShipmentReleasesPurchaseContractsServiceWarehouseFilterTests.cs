using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.ShipmentReleases;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.ShipmentReleases;

/// <summary>
/// Lista de liberações de destino da troca de liberação (GAC-1177 v2, Task 4): o destino pode
/// ser em QUALQUER armazém, então <c>WarehouseCode</c> precisa ser opcional — nulo ou vazio traz
/// liberações de todos os armazéns; informado, filtra como antes.
/// </summary>
public class ShipmentReleasesPurchaseContractsServiceWarehouseFilterTests
{
    private const string ItemCode = "SOJA";
    private const string CardCode = "F0001";

    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private readonly Branch _branch = new() { Code = "01", BranchName = "Matriz", ShortName = "MTZ" };

    private void AddContractWithRelease(string code, string warehouseCode, int rowId)
    {
        var contract = new PurchaseContract
        {
            Key = Guid.NewGuid(), Code = code, CardCode = CardCode, CardName = "FORNECEDOR",
            ItemCode = ItemCode, ItemName = "Soja", UnitOfMeasureCode = "KG",
            HarvestSeasonCode = "24/25", DeliveryLocationCode = warehouseCode, TotalVolume = 1000m,
        };
        _db.Context.PurchaseContracts.Add(contract);
        _db.Context.ShipmentReleases.Add(new ShipmentRelease
        {
            Key = Guid.NewGuid(), PurchaseContractKey = contract.Key, BranchCode = _branch.Code,
            RowId = rowId, DeliveryLocationCode = warehouseCode, DeliveryLocationName = warehouseCode,
            ReleasedQuantity = 100m, ShippedQuantity = 30m, Status = ReleaseStatus.Actived,
        });
    }

    private async Task<ICollection<Domain.Dtos.ShipmentRelesesPurchaseContractsResponseDto>> ExecuteAsync(
        string? warehouseCode)
    {
        _db.Context.Branchs.Add(_branch);
        AddContractWithRelease("PC-ARM01", "ARM01", rowId: 1);
        AddContractWithRelease("PC-ARM02", "ARM02", rowId: 1);
        await _db.Context.SaveChangesAsync();

        var service = new ShipmentReleasesPurchaseContractsService(_db, new FakeBusinessPartnerService(),
            NullLogger<ShipmentReleasesPurchaseContractsService>.Instance);

        return await service.ExecuteAsync(ItemCode, warehouseCode!);
    }

    [Fact]
    public async Task Null_WarehouseCode_ReturnsReleasesFromAllWarehouses()
    {
        var result = await ExecuteAsync(null);

        Assert.Equal(2, result.Count);
        Assert.Contains(result, r => r.DeliveryLocationCode == "ARM01");
        Assert.Contains(result, r => r.DeliveryLocationCode == "ARM02");
    }

    [Fact]
    public async Task Empty_WarehouseCode_ReturnsReleasesFromAllWarehouses()
    {
        var result = await ExecuteAsync(string.Empty);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public async Task Informed_WarehouseCode_StillFiltersToThatWarehouse()
    {
        var result = await ExecuteAsync("ARM01");

        var row = Assert.Single(result);
        Assert.Equal("ARM01", row.DeliveryLocationCode);
    }
}
