using System.Globalization;
using SiagroB1.Domain.Dtos;

namespace SiagroB1.Web.Functions.PurchaseFiscalReconciliations;

/// <summary>
/// Parâmetro string de Function OData chega com as aspas e é anulável (memória odata-action-string-params-are-nullable):
/// tira as aspas, desfaz o '' de escape, vazio vira null; data estrita aaaa-mm-dd (lição do GAC-1171).
/// </summary>
public static class PurchaseFiscalReconciliationParameters
{
    public static string? Text(string? raw)
    {
        if (raw is null) return null;
        var value = raw.Length >= 2 && raw[0] == '\'' && raw[^1] == '\'' ? raw[1..^1] : raw;
        value = value.Replace("''", "'").Trim();
        return value.Length == 0 ? null : value;
    }

    public static (FiscalReconciliationFilter? Filter, string? Error) Filter(
        string? cardCode, string? itemCode, string? contractCode, string? dateFrom, string? dateTo,
        string? issuerCardCode, bool includeFullyLinked)
    {
        if (!TryDate(dateFrom, out var from) || !TryDate(dateTo, out var to))
            return (null, "Informe as datas no formato aaaa-mm-dd.");

        return (new FiscalReconciliationFilter
        {
            CardCode = Text(cardCode) ?? "", ItemCode = Text(itemCode) ?? "", ContractCode = Text(contractCode),
            DateFrom = from, DateTo = to, IssuerCardCode = Text(issuerCardCode), IncludeFullyLinked = includeFullyLinked,
        }, null);
    }

    private static bool TryDate(string? raw, out DateTime? value)
    {
        value = null;
        var text = Text(raw);
        if (text is null) return true;
        if (!DateTime.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            return false;
        value = parsed;
        return true;
    }
}
