using System.Globalization;
using System.Text.Json;
using SiagroB1.Commons.Market;

namespace SiagroB1.Application.Services.Market.Cma;

/// <summary>
/// Converte uma cotação do feed da CMA em <see cref="MarketQuoteUpdate"/>.
/// Formato real (cma-data-feed, QuotesUpdateResponse / Quotes):
/// <c>{"datetime":"yyyy-MM-dd HH:mm:ss.fff","symbolId":{"sourceId":"1","symbol":"ZSF7"},"arrValues":[{"10":"1188.25S"},{"14":"1189.5"}]}</c>
/// onde cada item de arrValues é um objeto de uma chave hexadecimal: 01 variação %, 03 venda (ask), 10 último,
/// 14 compra (bid), 16 máxima, 17 mínima, 18 abertura, 1A fechamento anterior.
/// Também aceita o formato plano (<c>symbol, last10, bid14, askNumber03, percentageChange01, high16, low17,
/// open18, close1A, datetime</c>), que é o nome das colunas de CmaQuoteReponse. Campos ausentes viram nulo.
/// </summary>
public static class CmaQuoteMapper
{
    private static readonly string[] DateFormats =
    {
        "yyyy-MM-dd HH:mm:ss.fff", "yyyy-MM-dd HH:mm:ss", "yyyy-MM-ddTHH:mm:ss.fff", "yyyy-MM-ddTHH:mm:ss"
    };

    private static readonly TimeZoneInfo SaoPaulo = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");

    public static MarketQuoteUpdate? Map(JsonElement quote)
    {
        if (quote.ValueKind != JsonValueKind.Object)
            return null;

        var symbol = ReadSymbol(quote);
        if (string.IsNullOrWhiteSpace(symbol))
            return null;

        var wire = ReadArrValues(quote);

        string? Field(string flatName, string hexKey) =>
            wire.TryGetValue(hexKey, out var v) ? v : ReadString(quote, flatName);

        var last = CmaValueParser.Parse(Field("last10", "10"));

        return new MarketQuoteUpdate(
            symbol.Trim(),
            last.Value,
            CmaValueParser.Parse(Field("bid14", "14")).Value,
            CmaValueParser.Parse(Field("askNumber03", "03")).Value,
            CmaValueParser.Parse(Field("percentageChange01", "01")).Value,
            CmaValueParser.Parse(Field("high16", "16")).Value,
            CmaValueParser.Parse(Field("low17", "17")).Value,
            CmaValueParser.Parse(Field("open18", "18")).Value,
            CmaValueParser.Parse(Field("close1A", "1A")).Value,
            last.IsSettlement,
            ParseDate(ReadString(quote, "datetime")));
    }

    private static string? ReadSymbol(JsonElement quote)
    {
        if (quote.TryGetProperty("symbolId", out var id) && id.ValueKind == JsonValueKind.Object)
            return ReadString(id, "symbol");
        return ReadString(quote, "symbol");
    }

    private static Dictionary<string, string?> ReadArrValues(JsonElement quote)
    {
        var map = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        if (!quote.TryGetProperty("arrValues", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return map;

        foreach (var item in arr.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            foreach (var prop in item.EnumerateObject())
                map[prop.Name] = ToText(prop.Value);
        }
        return map;
    }

    private static string? ReadString(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var v) ? ToText(v) : null;

    private static string? ToText(JsonElement v) => v.ValueKind switch
    {
        JsonValueKind.String => v.GetString(),
        JsonValueKind.Number => v.GetRawText(),
        _ => null
    };

    // Horário do feed é local de São Paulo (sem fuso na mensagem); convertido para UTC.
    private static DateTime? ParseDate(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw) || !DateTime.TryParseExact(raw.Trim(), DateFormats,
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var local))
            return null;

        return TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local, DateTimeKind.Unspecified), SaoPaulo);
    }
}
