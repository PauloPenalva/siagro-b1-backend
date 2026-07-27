using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Infra;

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
            var releases = await LoadShipmentReleasesAsync(itemCode, warehouseCode);
            var suppliers = await LoadSuppliersAsync(releases);

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
    
    private async Task<Dictionary<string, SupplierInfo>> LoadSuppliersAsync(
        IEnumerable<ShipmentReleasesPurchaseContractsProjection> releases)
    {
        var cardCodes = releases.Select(r => r.CardCode).Distinct().ToList();

        return await businessPartnerService.LoadSuppliersAsync(cardCodes);
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
                sr.PurchaseContract.CardCode,
                sr.PurchaseContract.CardName,
            })
            .Select(g => new ShipmentReleasesPurchaseContractsProjection
            {
                Key = g.Key.Key,
                RowId = g.Key.RowId,
                PurchaseContractCode = g.Key.Code,
                BranchShortName = g.Key.ShortName,
                CardCode = g.Key.CardCode,
                CardName = g.Key.CardName,
                DeliveryLocationCode = g.Key.DeliveryLocationCode,
                DeliveryLocationName = g.Key.DeliveryLocationName,
                ItemCode = g.Key.ItemCode,
                ItemName = g.Key.ItemName,
                UnitOfMeasureCode = g.Key.UnitOfMeasureCode,
                ReleasedQuantity = g.Sum(x => x.ReleasedQuantity),
                UsedQuantity = g.Sum(sr => sr.ShippedQuantity)
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
            suppliers.TryGetValue(b.CardCode, out var supplier);

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
                TaxId = supplier?.TaxId,
                // O nome sai do campo desnormalizado do contrato: o parceiro mora no SAP e
                // nem sempre é resolvido pelo cadastro, mas o contrato sempre carrega o nome.
                FName = b.CardName ?? supplier?.CardName,
                FCode = b.CardCode,
                Notes = supplier?.Notes,
                City = supplier?.Address?.City,
                State = supplier?.Address?.State,
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
        public required string CardCode { get; init; }

        /// <summary>Nome desnormalizado em <c>PURCHASE_CONTRACTS.CardName</c>.</summary>
        public string? CardName { get; init; }
    }
    
}
