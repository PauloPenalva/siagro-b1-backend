using SiagroB1.Application.Services.SalesContracts;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Application.Services.ShipmentLoads;

public static class SapLinkReferencePrice
{
    /// <summary>
    /// Preço em R$/unidade que a alocação usaria; nulo quando o contrato não é em R$
    /// ou o preço confirmado é ≤ 0 (ex.: PAF sem fixação confirmada).
    /// </summary>
    public static async Task<decimal?> ResolveAsync(SalesContractsFixedVolumeService fixedVolume, SalesContract contract)
    {
        if ((contract.StandardCurrency ?? CurrencyType.Brl) != CurrencyType.Brl)
            return null;

        var price = await fixedVolume.ConfirmedUnitPriceAsync(contract.Key, contract.Price);
        return price > 0m ? price : null;
    }
}
