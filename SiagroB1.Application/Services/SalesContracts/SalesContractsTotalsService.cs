using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Dtos;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.SalesContracts;

public class SalesContractsTotalsService(AppDbContext context)
{
    public async Task<SalesContractTotalsResponseDto> GetTotals(Guid key)
    {
        // Include das fixações é obrigatório: TotalPrice agora soma as fixações Confirmed
        // e retorna 0 silenciosamente sem a navegação carregada (ver a armadilha do
        // computed-avaiablevolume-needs-nested-include).
        var ctr = await context.SalesContracts
            .AsNoTracking()
            .Include(x => x.PriceFixations)
            .FirstOrDefaultAsync(x => x.Key == key) ??
                  throw new KeyNotFoundException();
        
        return new SalesContractTotalsResponseDto
        {
            TotalPrice = ctr.TotalPrice,
            TotalVolume = ctr.TotalVolume,
        };
    }
}