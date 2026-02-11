using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.ShipmentReleases;

public class ShipmentReleasesBalanceService(
    IUnitOfWork db,
    SapErpDbContext sapDb,
    ILogger<ShipmentReleasesBalanceService> logger)
{
    public async Task<ICollection<ShipmentRelesesBalanceResponseDto>> ExecuteAsync(string itemCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(itemCode);

        try
        {
            var warehouses = await LoadWarehousesAsync();
            var balances = await LoadBalancesAsync(itemCode);

            return MapToDto(balances, warehouses);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Erro ao calcular saldo de liberações para o ItemCode {ItemCode}",
                itemCode);

            throw;
        }
    }

    #region Private Methods

    private async Task<Dictionary<string, WarehouseInfo>> LoadWarehousesAsync()
    {
        return await sapDb.BusinessPartners
            .AsNoTracking()
            .Where(x => x.QryGroup23 == "Y")
            .Select(bp => new WarehouseInfo
            {
                CardCode = bp.CardCode,
                CardFName = bp.CardFName,
                TaxId = bp.TaxId,
                Notes = bp.Notes,
                Address = bp.Addresses
                    .Where(a => a.AdresType == "S")
                    .OrderBy(a => a.AddressName)
                    .Select(a => new WarehouseAddress
                    {
                        City = a.City,
                        State = a.State
                    })
                    .FirstOrDefault()
            })
            .ToDictionaryAsync(x => x.CardCode);
    }

    private async Task<List<ShipmentReleaseBalanceProjection>> LoadBalancesAsync(string itemCode)
    {
        return await db.Context.ShipmentReleases
            .AsNoTracking()
            .Where(sr =>
                sr.PurchaseContract.ItemCode == itemCode &&
                sr.Status == ReleaseStatus.Actived)
            .GroupBy(sr => new
            {
                sr.DeliveryLocationCode,
                sr.DeliveryLocationName,
                sr.PurchaseContract.ItemCode,
                sr.PurchaseContract.ItemName,
                sr.PurchaseContract.UnitOfMeasureCode
            })
            .Select(g => new ShipmentReleaseBalanceProjection
            {
                DeliveryLocationCode = g.Key.DeliveryLocationCode,
                DeliveryLocationName = g.Key.DeliveryLocationName,
                ItemCode = g.Key.ItemCode,
                ItemName = g.Key.ItemName,
                UnitOfMeasureCode = g.Key.UnitOfMeasureCode,
                ReleasedQuantity = g.Sum(x => x.ReleasedQuantity),
                UsedQuantity = g.Sum(sr =>
                    sr.Transactions
                        .Where(t =>
                            t.TransactionStatus != StorageTransactionsStatus.Cancelled &&
                            (t.TransactionType == StorageTransactionType.SalesShipment ||
                             t.TransactionType == StorageTransactionType.SalesShipmentReturn))
                        .Sum(t => t.NetWeight))
            })
            .OrderBy(x => x.DeliveryLocationName)
            .ToListAsync();
    }

    private static List<ShipmentRelesesBalanceResponseDto> MapToDto(
        IEnumerable<ShipmentReleaseBalanceProjection> balances,
        IReadOnlyDictionary<string, WarehouseInfo> warehouses)
    {
        return balances.Select(b =>
        {
            warehouses.TryGetValue(b.DeliveryLocationCode, out var wh);

            return new ShipmentRelesesBalanceResponseDto
            {
                DeliveryLocationCode = b.DeliveryLocationCode,
                DeliveryLocationName = b.DeliveryLocationName,
                ItemCode = b.ItemCode,
                ItemName = b.ItemName,
                UnitOfMeasureCode = b.UnitOfMeasureCode,
                ReleasedQuantity = b.ReleasedQuantity,
                AvailableQuantity = b.ReleasedQuantity - b.UsedQuantity,
                TaxId = wh?.TaxId,
                FName = wh?.CardFName,
                Notes = wh?.Notes,
                City = wh?.Address?.City,
                State = wh?.Address?.State
            };
        }).ToList();
    }

    #endregion
    
    internal sealed class ShipmentReleaseBalanceProjection
    {
        public required string DeliveryLocationCode { get; init; }
        public required string DeliveryLocationName { get; init; }
        public required string ItemCode { get; init; }
        public required string ItemName { get; init; }
        public required string UnitOfMeasureCode { get; init; }
        public decimal ReleasedQuantity { get; init; }
        public decimal UsedQuantity { get; init; }
    }
    
    internal sealed class WarehouseAddress
    {
        public string? City { get; init; }
        public string? State { get; init; }
    }
    
    internal sealed class WarehouseInfo
    {
        public required string CardCode { get; init; }
        public string? CardFName { get; init; }
        public string? TaxId { get; init; }
        public string? Notes { get; init; }
        public WarehouseAddress? Address { get; init; }
    }
}
