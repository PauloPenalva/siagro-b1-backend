using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Dtos;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.SalesContracts;

public class SalesContractsTotalsService(AppDbContext context)
{
    public async Task<SalesContractTotalsResponseDto> GetTotals(Guid key)
    {
        var ctr = await context.SalesContracts
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Key == key) ??
                  throw new KeyNotFoundException();
        
        return new SalesContractTotalsResponseDto
        {
            TotalPrice = ctr.TotalPrice,
            TotalVolume = ctr.TotalVolume,
        };
    }
}