using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.ShipmentReleases;
using SiagroB1.Application.Services.WarehouseReconciliations;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Application.Tests.WarehouseReconciliations;

internal sealed partial class WarehouseReconciliationsTestContext
{
    public const string Producer = "F0001";

    public WarehouseReconciliationReleaseBalanceService ReleaseBalance() => new(Db);

    /// <summary>
    /// Contrato aprovado do produtor com UMA liberação no armazém de terceiros. É o grão que o
    /// sistema enxerga no armazém desde a revisão de 17/09 (spec §9): liberado − embarcado.
    /// </summary>
    public async Task<ShipmentRelease> SeedReleaseAsync(
        decimal released,
        DateTime releaseDate,
        ReleaseOrigin origin = ReleaseOrigin.Standard,
        ReleaseStatus status = ReleaseStatus.Actived,
        ContractStatus contractStatus = ContractStatus.Approved,
        string warehouse = ThirdPartyWarehouse)
    {
        var contract = new PurchaseContract
        {
            Key = Guid.NewGuid(),
            Code = $"PC-{++_seq:000}",
            CardCode = Producer,
            CardName = "Produtor Teste",
            ItemCode = Item,
            UnitOfMeasureCode = "KG",
            HarvestSeasonCode = "2026",
            DeliveryLocationCode = warehouse,
            Status = contractStatus,
            TotalVolume = released * 10,
            AllocatedVolume = 0m,
        };

        var release = new ShipmentRelease
        {
            Key = Guid.NewGuid(),
            PurchaseContractKey = contract.Key,
            DeliveryLocationCode = warehouse,
            DeliveryLocationName = "Armazém Terceiro",
            ReleaseDate = releaseDate.Date,
            ReleasedQuantity = released,
            ShippedQuantity = 0m,
            Status = status,
            Origin = origin,
        };

        Db.Context.PurchaseContracts.Add(contract);
        Db.Context.ShipmentReleases.Add(release);
        await Db.Context.SaveChangesAsync();
        return release;
    }

    /// <summary>
    /// Segunda liberação Standard/Actived do MESMO contrato de <paramref name="existing"/>, no mesmo
    /// armazém — para exercitar duas linhas de distribuição que concorrem pelo AllocatedVolume de um
    /// único contrato.
    /// </summary>
    public async Task<ShipmentRelease> SeedReleaseOnSameContractAsync(
        ShipmentRelease existing, decimal released, DateTime releaseDate)
    {
        var release = new ShipmentRelease
        {
            Key = Guid.NewGuid(),
            PurchaseContractKey = existing.PurchaseContractKey,
            DeliveryLocationCode = existing.DeliveryLocationCode,
            DeliveryLocationName = existing.DeliveryLocationName,
            ReleaseDate = releaseDate.Date,
            ReleasedQuantity = released,
            ShippedQuantity = 0m,
            Status = ReleaseStatus.Actived,
            Origin = ReleaseOrigin.Standard,
        };

        Db.Context.ShipmentReleases.Add(release);
        await Db.Context.SaveChangesAsync();
        return release;
    }

    /// <summary>Romaneio já confirmado contra a liberação, com o recálculo do embarcado.</summary>
    public async Task<StorageTransaction> SeedReleaseMovementAsync(
        ShipmentRelease release, StorageTransactionType type, decimal quantity, DateTime? date)
    {
        var transaction = await SeedStockAsync(type, quantity, date, warehouse: release.DeliveryLocationCode);
        transaction.ShipmentReleaseKey = release.Key;
        await Db.Context.SaveChangesAsync();
        await new ShipmentReleasesRecalculateShippedService(Db.Context).RecalculateAsync(release.Key);
        return transaction;
    }

    public Task<ShipmentRelease> ReloadReleaseAsync(Guid key) =>
        Db.Context.ShipmentReleases.AsNoTracking().SingleAsync(x => x.Key == key);
}
