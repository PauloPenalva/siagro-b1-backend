using SiagroB1.Domain.Interfaces;

namespace SiagroB1.Application.Services.Dimensions;

/// <summary>
/// Liga resolvedor e validador nos serviços de nota. Os dois são opcionais nos construtores
/// (nulo = sem efeito), como o <c>FiscalStockPeriodGuard</c>; em produção o DI sempre os injeta,
/// e ambos já retornam sem efeito fora do STANDALONE.
/// </summary>
public static class DimensionWiring
{
    public static async Task ApplyAsync(
        DimensionDefaultsResolver? resolver,
        DimensionCodeValidator? validator,
        IAccountingAssignment target,
        IAccountingAssignment? header,
        string? partnerCode,
        int? usageCode,
        IAccountingAssignment? previous,
        bool applyDefaults = true)
    {
        if (applyDefaults && resolver is not null)
            await resolver.ApplyAsync(target, header, partnerCode, usageCode);

        if (validator is not null)
            await validator.NormalizeAndValidateAsync(target, previous);
    }
}
