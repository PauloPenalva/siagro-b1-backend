using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.Taxes;

/// <summary>
/// Resolve a alíquota interna e o FCP do ICMS de um produto numa UF (spec 2026-10-17 §1). A alíquota do produto prevalece
/// sobre a da UF; um FCP nulo no produto herda o da UF; a regra da base vem sempre da UF. Sem cadastro da UF devolve
/// <c>null</c>: o chamador decide o que fazer (nunca se adivinha alíquota).
/// </summary>
public static class IcmsInternalRateResolver
{
    public static async Task<(decimal InternalRate, decimal FcpRate, DifalBaseRule BaseRule)?> ResolveAsync(
        AppDbContext ctx, string itemCode, string stateCode)
    {
        var state = (stateCode ?? string.Empty).Trim().ToUpperInvariant();

        var setting = await ctx.IcmsStateSettings.AsNoTracking().FirstOrDefaultAsync(s => s.StateCode == state);

        if (setting is null)
            return null;

        var item = (itemCode ?? string.Empty).Trim();
        var itemRate = await ctx.IcmsItemStateRates.AsNoTracking()
            .FirstOrDefaultAsync(r => r.ItemCode == item && r.StateCode == state);

        if (itemRate is null)
            return (setting.InternalRate, setting.FcpRate, setting.DifalBaseRule);

        return (itemRate.InternalRate, itemRate.FcpRate ?? setting.FcpRate, setting.DifalBaseRule);
    }
}
