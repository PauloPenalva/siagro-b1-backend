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

public class ShipmentReleasesBalanceServiceShippedTests
{
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

    [Fact]
    public async Task Balance_UsesShippedQuantityColumn()
    {
        var pc = new PurchaseContract
        {
            Key = Guid.NewGuid(), Code = "PC", CardCode = "F0001", ItemCode = "SOJA",
            UnitOfMeasureCode = "KG", HarvestSeasonCode = "24/25", DeliveryLocationCode = "01",
            ItemName = "Soja", TotalVolume = 1000m,
        };
        _db.Context.PurchaseContracts.Add(pc);
        _db.Context.ShipmentReleases.Add(new ShipmentRelease
        {
            Key = Guid.NewGuid(), PurchaseContractKey = pc.Key, DeliveryLocationCode = "01",
            DeliveryLocationName = "Matriz", ReleasedQuantity = 100m, ShippedQuantity = 30m,
            Status = ReleaseStatus.Actived,
        });
        await _db.Context.SaveChangesAsync();

        var service = new ShipmentReleasesBalanceService(_db, new EmptyWarehouseService(),
            NullLogger<ShipmentReleasesBalanceService>.Instance);

        var result = await service.ExecuteAsync("SOJA");

        var row = Assert.Single(result);
        Assert.Equal(100m, row.ReleasedQuantity);
        Assert.Equal(70m, row.AvailableQuantity); // 100 − 30
    }
}
