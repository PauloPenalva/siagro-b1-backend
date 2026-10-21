using SiagroB1.Domain.Interfaces;

namespace SiagroB1.Application.Services.Accounting;

/// <summary>
/// Liga resolvedor e validador nos serviços de nota. Os dois são opcionais nos construtores
/// (nulo = sem efeito), como o <c>FiscalStockPeriodGuard</c>; em produção o DI sempre os injeta,
/// e ambos já retornam sem efeito fora do STANDALONE.
/// </summary>
public static class AccountingAssignmentWiring
{
    public static async Task ApplyAsync(
        AccountingAssignmentDefaultsResolver? resolver,
        AccountingAssignmentValidator? validator,
        IAccountingAssignment target,
        IAccountingAssignment? header,
        string? partnerCode,
        int? usageCode,
        IAccountingAssignment? previous,
        bool applyDefaults = true,
        DateTime? documentDate = null)
    {
        if (applyDefaults && resolver is not null)
            await resolver.ApplyAsync(target, header, partnerCode, usageCode, documentDate);

        if (validator is not null)
            await validator.NormalizeAndValidateAsync(target, previous, documentDate);
    }
}
