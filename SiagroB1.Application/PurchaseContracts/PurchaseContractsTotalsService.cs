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
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Key == key) ??
                  throw new KeyNotFoundException();

        var totalReleased = await GetTotalShipmentReleases(key);
        var totalAvailableToRelease = ctr.TotalVolume - totalReleased;

        return new PurchaseContractTotalsDto
        {
            FixedVolume = ctr.FixedVolume,
            AvailableVolumeToPricing = ctr.AvailableVolumeToPricing,
            TotalPrice = ctr.TotalPrice,
            TotalTax = ctr.TotalTax,
            TotalShipmentReleases = totalReleased,
            TotalAvailableToRelease = totalAvailableToRelease
        };
    }

    private async Task<decimal> GetTotalShipmentReleases(Guid key)
    {
        return await context.ShipmentReleases
            .AsNoTracking()
            .Where(x => x.PurchaseContractKey == key && x.Status == ReleaseStatus.Approved)
            .SumAsync(x => x.ReleasedQuantity);
    }
}