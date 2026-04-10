using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Infra;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.ShipmentReleases;

public class ShipmentReleasesPurchaseContractsService(
    IUnitOfWork db,
    IBusinessPartnerService businessPartnerService,
    ILogger<ShipmentReleasesPurchaseContractsService> logger)
{
    public async Task<ICollection<ShipmentRelesesPurchaseContractsResponseDto>> ExecuteAsync(string itemCode, string warehouseCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(itemCode);

        try
        {
            var suppliers = await LoadSuppliersAsync();
            var releases = await LoadShipmentReleasesAsync(itemCode, warehouseCode);

            return MapToDto(releases, suppliers);
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
    
    private async Task<Dictionary<string, SupplierInfo>> LoadSuppliersAsync()
    {
        return await businessPartnerService.LoadSuppliersAsync();
       
    }

    private async Task<List<ShipmentReleasesPurchaseContractsProjection>> LoadShipmentReleasesAsync(string itemCode, string warehouseCode)
    {
        return await db.Context.ShipmentReleases
            .AsNoTracking()
            .Where(sr =>
                sr.PurchaseContract.ItemCode == itemCode &&
                sr.DeliveryLocationCode == warehouseCode &&
                sr.Status == ReleaseStatus.Actived)
            .GroupBy(sr => new
            {
                sr.Key,
                sr.DeliveryLocationCode,
                sr.DeliveryLocationName,
                sr.PurchaseContract.ItemCode,
                sr.PurchaseContract.ItemName,
                sr.PurchaseContract.UnitOfMeasureCode,
                sr.RowId,
                sr.PurchaseContract.Code,
                sr.Branch.ShortName,
            })
            .Select(g => new ShipmentReleasesPurchaseContractsProjection
            {
                Key = g.Key.Key,
                RowId = g.Key.RowId,
                PurchaseContractCode = g.Key.Code,
                BranchShortName = g.Key.ShortName,
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

    private static List<ShipmentRelesesPurchaseContractsResponseDto> MapToDto(
        IEnumerable<ShipmentReleasesPurchaseContractsProjection> balances,
        IReadOnlyDictionary<string, SupplierInfo> suppliers)
    {
        return balances.Select(b =>
        {
            suppliers.TryGetValue(b.DeliveryLocationCode, out var wh);

            return new ShipmentRelesesPurchaseContractsResponseDto
            {
                ShipmentReleaseKey = b.Key.ToString(),
                BranchShortName = b.BranchShortName,
                PurchaseContractCode = b.PurchaseContractCode,
                DeliveryLocationCode = b.DeliveryLocationCode,
                DeliveryLocationName = b.DeliveryLocationName,
                ItemCode = b.ItemCode,
                ItemName = b.ItemName,
                UnitOfMeasureCode = b.UnitOfMeasureCode,
                AvailableQuantity = b.ReleasedQuantity - b.UsedQuantity,
                TaxId = wh?.TaxId,
                FName = wh?.CardFName,
                Notes = wh?.Notes,
                City = wh?.Address?.City,
                State = wh?.Address?.State,
                RowId = b.RowId,
            };
        }).ToList();
    }

    #endregion
    
    internal sealed class ShipmentReleasesPurchaseContractsProjection
    {
        public required Guid Key { get; set; }
        
        public required string BranchShortName { get; set; }
        
        public required string PurchaseContractCode { get; set; }
        
        public int RowId { get; set; }
        
        public required string DeliveryLocationCode { get; init; }
        public required string DeliveryLocationName { get; init; }
        public required string ItemCode { get; init; }
        public required string ItemName { get; init; }
        public required string UnitOfMeasureCode { get; init; }
        public decimal ReleasedQuantity { get; init; }
        public decimal UsedQuantity { get; init; }
    }
    
}
