using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Interfaces;

namespace SiagroB1.Application.Services.WarehouseReconciliations;

/// <summary>Nomes desnormalizados para exibição. O parceiro é o próprio armazém.</summary>
public class WarehouseReconciliationsDescriptionService(
    IItemService items,
    IWarehouseService warehouses,
    IBusinessPartnerService partners)
{
    public async Task FillAsync(WarehouseReconciliation r)
    {
        r.CardCode = r.WarehouseCode;
        r.ItemName = (await items.GetByIdAsync(r.ItemCode))?.ItemName;
        r.WarehouseName = (await warehouses.GetByIdAsync(r.WarehouseCode))?.Name;
        r.CardName = (await partners.GetByIdAsync(r.CardCode))?.CardName;
    }
}
