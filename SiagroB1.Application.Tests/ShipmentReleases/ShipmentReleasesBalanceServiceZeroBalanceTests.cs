using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.ShipmentReleases;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Domain.Models;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.ShipmentReleases;

/// <summary>
/// Cobre a primeira tela da Expedição de Grãos (seleção de armazém): armazém sem saldo a
/// embarcar não pode aparecer na lista. O filtro é por liberação, antes do agrupamento, para
/// que o saldo exibido no armazém seja exatamente a soma dos contratos da tela seguinte.
/// </summary>
public class ShipmentReleasesBalanceServiceZeroBalanceTests
{
    private const string ItemCode = "SOJA";

    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private sealed class EmptyWarehouseService : IWarehouseService
    {
        public Task<IEnumerable<WarehouseModel>> GetAllAsync() => throw new NotImplementedException();
        public Task<WarehouseModel?> GetByIdAsync(string code) => throw new NotImplementedException();
        public Task<WarehouseModel> CreateAsync(WarehouseModel model) => throw new NotImplementedException();
        public Task<WarehouseModel?> UpdateAsync(string code, WarehouseModel model) => throw new NotImplementedException();
        public Task<bool> DeleteAsync(string code) => throw new NotImplementedException();
        public IQueryable<WarehouseModel> QueryAll() => throw new NotImplementedException();
        public Task<Dictionary<string, WarehouseInfo>> LoadWarehousesAsync() => Task.FromResult(new Dictionary<string, WarehouseInfo>());
    }

    private readonly PurchaseContract _contract = new()
    {
        Key = Guid.NewGuid(), Code = "PC-1", CardCode = "F0001", ItemCode = ItemCode,
        ItemName = "Soja", UnitOfMeasureCode = "KG", HarvestSeasonCode = "24/25",
        DeliveryLocationCode = "ARM01", TotalVolume = 1000m,
    };

    private void AddRelease(string warehouseCode, decimal released, decimal shipped)
    {
        _db.Context.ShipmentReleases.Add(new ShipmentRelease
        {
            Key = Guid.NewGuid(), PurchaseContractKey = _contract.Key,
            DeliveryLocationCode = warehouseCode, DeliveryLocationName = $"Armazem {warehouseCode}",
            ReleasedQuantity = released, ShippedQuantity = shipped, Status = ReleaseStatus.Actived,
        });
    }

    private async Task<ICollection<ShipmentRelesesBalanceResponseDto>> ExecuteAsync()
    {
        _db.Context.PurchaseContracts.Add(_contract);
        await _db.Context.SaveChangesAsync();

        var service = new ShipmentReleasesBalanceService(_db, new EmptyWarehouseService(),
            NullLogger<ShipmentReleasesBalanceService>.Instance);

        return await service.ExecuteAsync(ItemCode);
    }

    [Fact]
    public async Task Warehouse_IsOmitted_WhenReleaseIsFullyShipped()
    {
        AddRelease("ARM01", released: 100m, shipped: 100m);

        Assert.Empty(await ExecuteAsync());
    }

    [Fact]
    public async Task Warehouse_IsOmitted_WhenBalanceIsNegative()
    {
        AddRelease("ARM01", released: 100m, shipped: 120m);

        Assert.Empty(await ExecuteAsync());
    }

    [Fact]
    public async Task Warehouse_SumsOnlyReleasesWithBalance()
    {
        AddRelease("ARM01", released: 100m, shipped: 0m);
        AddRelease("ARM01", released: 50m, shipped: 50m);

        var row = Assert.Single(await ExecuteAsync());

        Assert.Equal("ARM01", row.DeliveryLocationCode);
        Assert.Equal(100m, row.ReleasedQuantity);
        Assert.Equal(100m, row.AvailableQuantity);
    }

    [Fact]
    public async Task OnlyWarehousesWithBalance_AreReturned()
    {
        AddRelease("ARM01", released: 100m, shipped: 30m);
        AddRelease("ARM02", released: 80m, shipped: 80m);

        var row = Assert.Single(await ExecuteAsync());

        Assert.Equal("ARM01", row.DeliveryLocationCode);
        Assert.Equal(70m, row.AvailableQuantity);
    }
}
