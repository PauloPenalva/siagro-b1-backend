using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Dtos;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.PurchaseContracts;

public class PurchaseContractsTotalsService(AppDbContext context)
{
    public async Task<PurchaseContractTotalsDto> GetTotals(Guid key)
    {
        var ctr = await context.PurchaseContracts
                      .Include(x => x.Taxes)
                      .ThenInclude(x => x.Tax)
                      .Include(x => x.PriceFixations)
                      .Include(x => x.ShipmentReleases)
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Key == key) ??
                  throw new KeyNotFoundException();
        
        return new PurchaseContractTotalsDto
        {
            FixedVolume = ctr.FixedVolume,
            AvailableVolumeToPricing = ctr.AvailableVolumeToPricing,
            TotalPrice = ctr.TotalPrice,
            TotalTax = ctr.TotalTax,
            TotalShipmentReleases = ctr.TotalShipmentReleases,
            TotalAvailableToRelease = ctr.TotalAvailableToRelease
        };
    }
}