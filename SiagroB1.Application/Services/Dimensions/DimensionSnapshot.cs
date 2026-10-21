using Microsoft.EntityFrameworkCore.ChangeTracking;
using SiagroB1.Domain.Interfaces;

namespace SiagroB1.Application.Services.Dimensions;

/// <summary>
/// Cópia solta das cinco dimensões e da conta contábil. No PATCH a entidade chega já rastreada e
/// mutada, então o "antes" que o validador compara precisa ser capturado do rastreador (ou do banco)
/// antes de a mudança ser aplicada.
/// </summary>
public sealed class DimensionSnapshot : IDimensionFields
{
    public string? Dim1Code { get; set; }
    public string? Dim2Code { get; set; }
    public string? Dim3Code { get; set; }
    public string? Dim4Code { get; set; }
    public string? Dim5Code { get; set; }
    public string? LedgerAccountCode { get; set; }

    public static DimensionSnapshot From(IDimensionFields source)
    {
        var snapshot = new DimensionSnapshot { LedgerAccountCode = source.LedgerAccountCode };
        CopyDims(source, snapshot);
        return snapshot;
    }

    /// <summary>O gravado, lido do OriginalValues do rastreador (vale para PATCH aliasado e PUT).</summary>
    public static DimensionSnapshot FromOriginal(PropertyValues original) => new()
    {
        Dim1Code = (string?)original[nameof(Dim1Code)],
        Dim2Code = (string?)original[nameof(Dim2Code)],
        Dim3Code = (string?)original[nameof(Dim3Code)],
        Dim4Code = (string?)original[nameof(Dim4Code)],
        Dim5Code = (string?)original[nameof(Dim5Code)],
        LedgerAccountCode = (string?)original[nameof(LedgerAccountCode)],
    };

    public static void Copy(IDimensionFields source, IDimensionFields target)
    {
        CopyDims(source, target);
        target.LedgerAccountCode = source.LedgerAccountCode;
    }

    private static void CopyDims(IDimensionFields source, IDimensionFields target)
    {
        for (var n = 1; n <= 5; n++)
            target.SetDim(n, source.GetDim(n));
    }
}

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
        IDimensionFields target,
        IDimensionFields? header,
        string? partnerCode,
        int? usageCode,
        IDimensionFields? previous,
        bool applyDefaults = true)
    {
        if (applyDefaults && resolver is not null)
            await resolver.ApplyAsync(target, header, partnerCode, usageCode);

        if (validator is not null)
            await validator.NormalizeAndValidateAsync(target, previous);
    }
}
