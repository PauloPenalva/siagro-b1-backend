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
    IWarehouseService warehouseService,
    ILogger<ShipmentReleasesPurchaseContractsService> logger)
{
    public async Task<ICollection<ShipmentRelesesPurchaseContractsResponseDto>> ExecuteAsync(string itemCode, string? warehouseCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(itemCode);

        try
        {
            var releases = await LoadShipmentReleasesAsync(itemCode, warehouseCode);
            var suppliers = await LoadSuppliersAsync(releases);
            // Nome fantasia do armazém vem do cadastro VIVO (GAC-1185): o nome gravado na
            // liberação é a razão social da época e não acompanha o AliasName do SAP.
            var warehouses = await warehouseService.LoadWarehousesAsync();

            return MapToDto(releases, suppliers, warehouses);
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

    private async Task<List<ShipmentReleasesPurchaseContractsProjection>> LoadShipmentReleasesAsync(string itemCode, string? warehouseCode)
    {
        // Troca de liberação (GAC-1177 v2): o destino pode ser em QUALQUER armazém, então a
        // tela chama esta function sem WarehouseCode — nulo/vazio não filtra por armazém.
        var hasWarehouseFilter = !string.IsNullOrWhiteSpace(warehouseCode);

        return await db.Context.ShipmentReleases
            .AsNoTracking()
            .Where(sr =>
                sr.PurchaseContract.ItemCode == itemCode &&
                (!hasWarehouseFilter || sr.DeliveryLocationCode == warehouseCode) &&
                sr.Status == ReleaseStatus.Actived &&
                // Mesma regra da lista de armazéns: contrato sem saldo a embarcar fica fora.
                sr.ReleasedQuantity - sr.ShippedQuantity > 0)
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
                sr.Origin,
                sr.PurchaseContract.StandardCashFlowDate,
                sr.PurchaseContract.LogisticRegionCode,
                LogisticRegionName = sr.PurchaseContract.LogisticRegion!.Name,
            })
            .Select(g => new ShipmentReleasesPurchaseContractsProjection
            {
                Key = g.Key.Key,
                RowId = g.Key.RowId,
                PurchaseContractCode = g.Key.Code,
                BranchShortName = g.Key.ShortName,
                CardCode = g.Key.CardCode,
                CardName = g.Key.CardName,
                Origin = g.Key.Origin,
                StandardCashFlowDate = g.Key.StandardCashFlowDate,
                LogisticRegionCode = g.Key.LogisticRegionCode,
                LogisticRegionName = g.Key.LogisticRegionName,
                DeliveryLocationCode = g.Key.DeliveryLocationCode,
                DeliveryLocationName = g.Key.DeliveryLocationName,
                ItemCode = g.Key.ItemCode,
                ItemName = g.Key.ItemName,
                UnitOfMeasureCode = g.Key.UnitOfMeasureCode,
                ReleasedQuantity = g.Sum(x => x.ReleasedQuantity),
                UsedQuantity = g.Sum(sr => sr.ShippedQuantity)
            })
            // Previsão de pagamento crescente: o contrato mais próximo de vencer embarca
            // primeiro. Contrato sem data vai para o FIM (o SQL Server ordena NULL antes
            // em ASC, e o que interessa aqui é o que tem prazo). Local de entrega segue
            // como desempate, mantendo a ordem anterior dentro da mesma data.
            .OrderBy(x => x.StandardCashFlowDate == null)
            .ThenBy(x => x.StandardCashFlowDate)
            .ThenBy(x => x.DeliveryLocationName)
            .ToListAsync();
    }

    private static List<ShipmentRelesesPurchaseContractsResponseDto> MapToDto(
        IEnumerable<ShipmentReleasesPurchaseContractsProjection> balances,
        IReadOnlyDictionary<string, SupplierInfo> suppliers,
        IReadOnlyDictionary<string, WarehouseInfo> warehouses)
    {
        return balances.Select(b =>
        {
            suppliers.TryGetValue(b.CardCode, out var supplier);
            warehouses.TryGetValue(b.DeliveryLocationCode, out var warehouse);

            return new ShipmentRelesesPurchaseContractsResponseDto
            {
                ShipmentReleaseKey = b.Key.ToString(),
                BranchShortName = b.BranchShortName,
                PurchaseContractCode = b.PurchaseContractCode,
                DeliveryLocationCode = b.DeliveryLocationCode,
                DeliveryLocationName = b.DeliveryLocationName,
                // AliasName é opcional no SAP: sem ele vale a razão social do cadastro; armazém
                // fora do cadastro fica com o nome gravado na liberação. Nunca vazio.
                WarehouseFName = FirstFilled(warehouse?.CardFName, warehouse?.CardName, b.DeliveryLocationName),
                LogisticRegionCode = b.LogisticRegionCode,
                LogisticRegionName = b.LogisticRegionName,
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
                IsOwnershipTransfer = b.Origin == ReleaseOrigin.OwnershipTransfer,
                Origin = (int)b.Origin,
                StandardCashFlowDate = b.StandardCashFlowDate,
            };
        }).ToList();
    }

    private static string? FirstFilled(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

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

        public ReleaseOrigin Origin { get; init; }

        public DateTime? StandardCashFlowDate { get; init; }

        public string? LogisticRegionCode { get; init; }

        public string? LogisticRegionName { get; init; }
    }
    
}
