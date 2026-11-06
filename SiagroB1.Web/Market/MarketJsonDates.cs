using System.Globalization;
using System.Text.Json.Nodes;

namespace SiagroB1.Web.Market;

/// <summary>Datas de negócio (coluna DATE) nas respostas REST do Mercado saem como <c>yyyy-MM-dd</c> (ou null).</summary>
public static class MarketJsonDates
{
    public static void AsDateOnly(JsonObject node, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (node[key] is not JsonValue value)
                continue;
            if (value.TryGetValue<DateTime>(out var date)
                || (value.TryGetValue<string>(out var text)
                    && DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out date)))
                node[key] = date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }
    }
}
