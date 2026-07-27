using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.ShipmentReleases;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.ShipmentReleases;

/// <summary>
/// Cobre a segunda tela da Expedição de Grãos (seleção do contrato de compra): contrato cuja
/// liberação já foi totalmente embarcada não pode aparecer na lista.
/// </summary>
public class ShipmentReleasesPurchaseContractsServiceZeroBalanceTests
{
    private const string ItemCode = "SOJA";
    private const string WarehouseCode = "ARM01";
    private const string CardCode = "F0001";

    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private readonly Branch _branch = new() { Code = "01", BranchName = "Matriz", ShortName = "MTZ" };

    private readonly PurchaseContract _contract = new()
    {
        Key = Guid.NewGuid(), Code = "PC-1", CardCode = CardCode, CardName = "FORNECEDOR",
        ItemCode = ItemCode, ItemName = "Soja", UnitOfMeasureCode = "KG",
        HarvestSeasonCode = "24/25", DeliveryLocationCode = WarehouseCode, TotalVolume = 1000m,
    };

    private void AddRelease(int rowId, decimal released, decimal shipped)
    {
        _db.Context.ShipmentReleases.Add(new ShipmentRelease
        {
            Key = Guid.NewGuid(), PurchaseContractKey = _contract.Key, BranchCode = _branch.Code,
            RowId = rowId, DeliveryLocationCode = WarehouseCode, DeliveryLocationName = "Armazem 01",
            ReleasedQuantity = released, ShippedQuantity = shipped, Status = ReleaseStatus.Actived,
        });
    }

    private async Task<ICollection<ShipmentRelesesPurchaseContractsResponseDto>> ExecuteAsync()
    {
        _db.Context.Branchs.Add(_branch);
        _db.Context.PurchaseContracts.Add(_contract);
        await _db.Context.SaveChangesAsync();

        var service = new ShipmentReleasesPurchaseContractsService(_db, new FakeBusinessPartnerService(),
            NullLogger<ShipmentReleasesPurchaseContractsService>.Instance);

        return await service.ExecuteAsync(ItemCode, WarehouseCode);
    }

    [Fact]
    public async Task Contract_IsOmitted_WhenReleaseIsFullyShipped()
    {
        AddRelease(rowId: 1, released: 100m, shipped: 100m);

        Assert.Empty(await ExecuteAsync());
    }

    [Fact]
    public async Task Contract_IsOmitted_WhenBalanceIsNegative()
    {
        AddRelease(rowId: 1, released: 100m, shipped: 120m);

        Assert.Empty(await ExecuteAsync());
    }

    [Fact]
    public async Task OnlyReleasesWithBalance_AreReturned()
    {
        AddRelease(rowId: 1, released: 100m, shipped: 100m);
        AddRelease(rowId: 2, released: 80m, shipped: 30m);

        var row = Assert.Single(await ExecuteAsync());

        Assert.Equal(2, row.RowId);
        Assert.Equal(50m, row.AvailableQuantity);
    }
}
