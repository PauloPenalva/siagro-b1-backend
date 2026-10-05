using System.Globalization;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;

namespace SiagroB1.Application.Services.Taxes;

/// <summary>
/// Conferência da devolução própria contra a linha de origem (venda ou compra) (spec §7, abordagem A): a devolução é
/// calculada pela natureza de entrada, e esta conferência garante que o resultado reproduz a
/// tributação da operação original (venda ou compra). PIS/COFINS ficam de fora de propósito: na entrada o CST é outro.
/// </summary>
public static class NfeReturnConference
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public static void Ensure(INfeTaxedLine returned, INfeTaxedLine origin, string usageName, string operation)
    {
        Text(returned, usageName, operation, "CST do ICMS", origin.CstIcms, returned.CstIcms);
        Number(returned, usageName, operation, "alíquota do ICMS", origin.IcmsRate, returned.IcmsRate);
        Number(returned, usageName, operation, "redução da base do ICMS", origin.IcmsBaseReduction, returned.IcmsBaseReduction);
        Number(returned, usageName, operation, "diferimento do ICMS", origin.IcmsDeferral, returned.IcmsDeferral);
        Text(returned, usageName, operation, "cBenef", origin.IcmsBenefitCode, returned.IcmsBenefitCode);
        Text(returned, usageName, operation, "CST do IBS/CBS", origin.IbsCbsCst, returned.IbsCbsCst);
        Text(returned, usageName, operation, "classificação do IBS/CBS", origin.IbsCbsClassCode, returned.IbsCbsClassCode);
        Number(returned, usageName, operation, "alíquota da CBS", origin.CbsRate, returned.CbsRate);
        Number(returned, usageName, operation, "redução da CBS", origin.CbsRateReduction, returned.CbsRateReduction);
        Number(returned, usageName, operation, "alíquota do IBS estadual", origin.IbsStateRate, returned.IbsStateRate);
        Number(returned, usageName, operation, "alíquota do IBS municipal", origin.IbsMunicipalRate, returned.IbsMunicipalRate);
        Number(returned, usageName, operation, "redução do IBS", origin.IbsRateReduction, returned.IbsRateReduction);
    }

    private static void Text(INfeTaxedLine returned, string usageName, string operation, string field, string? origin, string? ret)
    {
        var a = Blank(origin);
        var b = Blank(ret);

        if (!string.Equals(a, b, StringComparison.OrdinalIgnoreCase))
            Fail(returned, usageName, operation, field, a ?? "(vazio)", b ?? "(vazio)");
    }

    private static void Number(INfeTaxedLine returned, string usageName, string operation, string field, decimal origin, decimal ret)
    {
        if (origin != ret)
            Fail(returned, usageName, operation, field, origin.ToString("0.####", PtBr), ret.ToString("0.####", PtBr));
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static void Fail(INfeTaxedLine returned, string usageName, string operation, string field, string origin, string ret) =>
        throw new DefaultException(
            $"Item {returned.ItemCode}: a natureza de devolução {usageName} não reproduz a tributação da {operation} — " +
            $"{field}: {operation} {origin}, devolução {ret}.");
}
