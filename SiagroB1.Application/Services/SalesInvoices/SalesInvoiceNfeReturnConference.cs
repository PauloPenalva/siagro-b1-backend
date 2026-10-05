using System.Globalization;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Application.Services.SalesInvoices;

/// <summary>
/// Conferência da devolução própria contra a linha vendida (spec §7, abordagem A): a devolução é
/// calculada pela natureza de entrada, e esta conferência garante que o resultado reproduz a
/// tributação da venda. PIS/COFINS ficam de fora de propósito: na entrada o CST é outro.
/// </summary>
public static class SalesInvoiceNfeReturnConference
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    public static void Ensure(SalesInvoiceItem returned, SalesInvoiceItem sold, string usageName)
    {
        Text(returned, usageName, "CST do ICMS", sold.CstIcms, returned.CstIcms);
        Number(returned, usageName, "alíquota do ICMS", sold.IcmsRate, returned.IcmsRate);
        Number(returned, usageName, "redução da base do ICMS", sold.IcmsBaseReduction, returned.IcmsBaseReduction);
        Number(returned, usageName, "diferimento do ICMS", sold.IcmsDeferral, returned.IcmsDeferral);
        Text(returned, usageName, "cBenef", sold.IcmsBenefitCode, returned.IcmsBenefitCode);
        Text(returned, usageName, "CST do IBS/CBS", sold.IbsCbsCst, returned.IbsCbsCst);
        Text(returned, usageName, "classificação do IBS/CBS", sold.IbsCbsClassCode, returned.IbsCbsClassCode);
        Number(returned, usageName, "alíquota da CBS", sold.CbsRate, returned.CbsRate);
        Number(returned, usageName, "redução da CBS", sold.CbsRateReduction, returned.CbsRateReduction);
        Number(returned, usageName, "alíquota do IBS estadual", sold.IbsStateRate, returned.IbsStateRate);
        Number(returned, usageName, "alíquota do IBS municipal", sold.IbsMunicipalRate, returned.IbsMunicipalRate);
        Number(returned, usageName, "redução do IBS", sold.IbsRateReduction, returned.IbsRateReduction);
    }

    private static void Text(SalesInvoiceItem returned, string usageName, string field, string? sale, string? ret)
    {
        var a = Blank(sale);
        var b = Blank(ret);

        if (!string.Equals(a, b, StringComparison.OrdinalIgnoreCase))
            Fail(returned, usageName, field, a ?? "(vazio)", b ?? "(vazio)");
    }

    private static void Number(SalesInvoiceItem returned, string usageName, string field, decimal sale, decimal ret)
    {
        if (sale != ret)
            Fail(returned, usageName, field, sale.ToString("0.####", PtBr), ret.ToString("0.####", PtBr));
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static void Fail(SalesInvoiceItem returned, string usageName, string field, string sale, string ret) =>
        throw new DefaultException(
            $"Item {returned.ItemCode}: a natureza de devolução {usageName} não reproduz a tributação da venda — " +
            $"{field}: venda {sale}, devolução {ret}.");
}
