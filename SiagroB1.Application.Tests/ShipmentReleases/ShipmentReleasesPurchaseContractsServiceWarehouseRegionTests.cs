using Microsoft.Extensions.Logging.Abstractions;
using SiagroB1.Application.Services.ShipmentReleases;
using SiagroB1.Application.Tests.Support;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Tests.ShipmentReleases;

/// <summary>
/// GAC-1185 (rodada 3): a seleção de liberação de compra do planejamento da carga mostra o nome
/// fantasia do armazém (AliasName, lido do cadastro vivo) e a Região Logística do contrato.
/// </summary>
public class ShipmentReleasesPurchaseContractsServiceWarehouseRegionTests
{
    private const string ItemCode = "SOJA";
    private const string WarehouseCode = "ARM01";

    private readonly UnitOfWork _db = TestDb.CreateUnitOfWork();

    private async Task SeedAsync(string? regionCode)
    {
        var branch = new Branch { Code = "01", BranchName = "Matriz", ShortName = "MTZ" };

        if (regionCode != null)
        {
            _db.Context.Add(new LogisticRegion { Code = regionCode, Name = "SANTOS / GUARUJA" });
        }

        var contract = new PurchaseContract
        {
            Key = Guid.NewGuid(), Code = "PC-1", CardCode = "F001", CardName = "FORNECEDOR",
            ItemCode = ItemCode, ItemName = "Soja", UnitOfMeasureCode = "KG",
            HarvestSeasonCode = "24/25", DeliveryLocationCode = WarehouseCode, TotalVolume = 1000m,
            LogisticRegionCode = regionCode,
        };

        _db.Context.Branchs.Add(branch);
        _db.Context.PurchaseContracts.Add(contract);
        _db.Context.ShipmentReleases.Add(new ShipmentRelease
        {
            Key = Guid.NewGuid(), PurchaseContractKey = contract.Key, BranchCode = branch.Code,
            DeliveryLocationCode = WarehouseCode, DeliveryLocationName = "NOME NA LIBERACAO",
            ReleasedQuantity = 100m, ShippedQuantity = 0m, Status = ReleaseStatus.Actived,
        });
        await _db.Context.SaveChangesAsync();
    }

    private async Task<ShipmentRelesesPurchaseContractsResponseDto> RowAsync(WarehouseInfo? warehouse)
    {
        var warehouses = new FakeWarehouseService();

        if (warehouse != null)
        {
            warehouses.Warehouses[warehouse.CardCode] = warehouse;
        }

        var service = new ShipmentReleasesPurchaseContractsService(_db, new FakeBusinessPartnerService(),
            warehouses, NullLogger<ShipmentReleasesPurchaseContractsService>.Instance);

        return Assert.Single(await service.ExecuteAsync(ItemCode, WarehouseCode));
    }

    [Fact]
    public async Task WarehouseFName_UsesAliasName_WhenFilled()
    {
        await SeedAsync(null);

        var row = await RowAsync(new WarehouseInfo
        {
            CardCode = WarehouseCode, CardName = "RAZAO SOCIAL LTDA", CardFName = "FANTASIA",
        });

        Assert.Equal("FANTASIA", row.WarehouseFName);
    }

    [Fact]
    public async Task WarehouseFName_FallsBackToCardName_WhenAliasNameIsEmpty()
    {
        await SeedAsync(null);

        var row = await RowAsync(new WarehouseInfo
        {
            CardCode = WarehouseCode, CardName = "RAZAO SOCIAL LTDA", CardFName = " ",
        });

        Assert.Equal("RAZAO SOCIAL LTDA", row.WarehouseFName);
    }

    [Fact]
    public async Task WarehouseFName_FallsBackToReleaseName_WhenWarehouseIsNotInRegistry()
    {
        await SeedAsync(null);

        var row = await RowAsync(null);

        Assert.Equal("NOME NA LIBERACAO", row.WarehouseFName);
    }

    [Fact]
    public async Task LogisticRegion_ComesFromContract()
    {
        await SeedAsync("001");

        var row = await RowAsync(null);

        Assert.Equal("001", row.LogisticRegionCode);
        Assert.Equal("SANTOS / GUARUJA", row.LogisticRegionName);
    }

    [Fact]
    public async Task LogisticRegion_IsNull_WhenContractHasNone()
    {
        await SeedAsync(null);

        var row = await RowAsync(null);

        Assert.Null(row.LogisticRegionCode);
        Assert.Null(row.LogisticRegionName);
    }
}
