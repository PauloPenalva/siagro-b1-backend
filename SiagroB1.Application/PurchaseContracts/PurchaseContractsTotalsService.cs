using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Dtos;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.PurchaseContracts;

public class PurchaseContractsTotalsService(AppDbContext context)
{
    public async Task<PurchaseContractTotalsResponseDto> GetTotals(Guid key)
    {
        var ctr = await context.PurchaseContracts
                      .Include(x => x.Taxes)
                      .ThenInclude(x => x.Tax)
                      .Include(x => x.PriceFixations)
                      .Include(x => x.ShipmentReleases)
                      .Include(x => x.Allocations)
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Key == key) ??
                  throw new KeyNotFoundException();
        
        return new PurchaseContractTotalsResponseDto
        {
            FixedVolume = ctr.FixedVolume,
            AvailableVolumeToPricing = ctr.AvailableVolumeToPricing,
            TotalPrice = ctr.TotalPrice,
            TotalTax = ctr.TotalTax,
            TotalShipmentReleases = ctr.TotalShipmentReleases,
            TotalAvailableToRelease = ctr.TotalAvailableToRelease,
            TotalStandard = ctr.TotalStandard,
            TotalVolume = ctr.TotalVolume,
            AvaiableVolume = ctr.AvaiableVolume,
        };
    }
}