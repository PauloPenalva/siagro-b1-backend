using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace SiagroB1.Web.Market;

public static class MarketModelStateExtensions
{
    /// <summary>
    /// Campo que o servidor preenche (ex.: o código da commodity dos filhos, que vem da rota ou do cabeçalho) não pode
    /// reprovar a validação por "obrigatório": tira do ModelState toda entrada daquele campo, na raiz ou nos filhos
    /// (<c>CommodityCode</c>, <c>commodityCode</c>, <c>Months[0].CommodityCode</c>).
    /// </summary>
    public static void ForgetServerFilled(this ModelStateDictionary modelState, string propertyName)
    {
        var keys = modelState.Keys
            .Where(k => k.Equals(propertyName, StringComparison.OrdinalIgnoreCase)
                        || k.EndsWith("." + propertyName, StringComparison.OrdinalIgnoreCase))
            .ToList();
        foreach (var key in keys)
            modelState.Remove(key);
    }
}
