using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Fiscal.Taxes;
using SiagroB1.Infra;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.PurchaseContracts;

public class PurchaseContractsTotalsService(AppDbContext context, IUsage usageService, IConfiguration configuration)
{
    public async Task<PurchaseContractTotalsResponseDto> GetTotals(Guid key)
    {
        var ctr = await context.PurchaseContracts
                      .Include(x => x.Taxes)
                      .ThenInclude(x => x.Tax)
                      .Include(x => x.PriceFixations)
                      .Include(x => x.ShipmentReleases)
                      .Include(x => x.Allocations)
                      .ThenInclude(a => a.StorageTransaction)
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Key == key) ??
                  throw new KeyNotFoundException();

        var dto = new PurchaseContractTotalsResponseDto
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
            WashedOutVolume = ctr.WashedOutVolume,
        };

        if (ErpMode.IsStandalone(configuration))
            await ApplyFunruralAsync(dto, key);

        return dto;
    }

    /// <summary>
    /// Spec 2026-10-13 regra 9: em STANDALONE a grade digitada (PURCHASE_CONTRACTS_TAXES) deixa de contar e
    /// "Total Impostos" passa a ser o Funrural da natureza do complemento fiscal, pelo MESMO cálculo da NF de
    /// Entrada (FunruralCalculator), sobre o total das fixações confirmadas. Alíquota lida ao vivo — natureza
    /// desativada depois de escolhida continua valendo. Sem natureza: tudo zero e líquido = base.
    /// </summary>
    private async Task ApplyFunruralAsync(PurchaseContractTotalsResponseDto dto, Guid key)
    {
        var usageCode = await context.PurchaseContractFiscalComplements.AsNoTracking()
            .Where(x => x.PurchaseContractKey == key)
            .Select(x => x.UsageCode)
            .FirstOrDefaultAsync();

        var usage = usageCode is { } code ? await usageService.GetByIdAsync(code) : null;
        var funrural = FunruralCalculator.Calculate(
            dto.TotalPrice, usage?.FunruralInssRate, usage?.FunruralRatRate, usage?.FunruralSenarRate);

        dto.FunruralBase = dto.TotalPrice;
        dto.FunruralInssRate = funrural.InssRate;
        dto.FunruralRatRate = funrural.RatRate;
        dto.FunruralSenarRate = funrural.SenarRate;
        dto.FunruralInssValue = funrural.InssValue;
        dto.FunruralRatValue = funrural.RatValue;
        dto.FunruralSenarValue = funrural.SenarValue;
        dto.TotalFunrural = funrural.Total;
        dto.NetToProducer = dto.TotalPrice - funrural.Total;
        dto.TotalTax = funrural.Total;
    }
}
