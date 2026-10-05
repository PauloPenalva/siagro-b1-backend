using System.Collections;
using System.Globalization;
using Microsoft.AspNetCore.OData.Formatter;

namespace SiagroB1.Web.Actions.Nfe;

/// <summary>Leitura compartilhada dos parâmetros das actions "Devolver" (venda e compra).</summary>
public static class NfeReturnActionParameters
{
    public static List<int> ItemNumbers(ODataActionParameters parameters)
    {
        if (!parameters.TryGetValue("ItemNumbers", out var value) || value is not IEnumerable sequence)
            return [];

        var result = new List<int>();

        foreach (var item in sequence)
        {
            result.Add(item switch
            {
                int i => i,
                long l => (int)l,
                double d => (int)d,
                _ => int.TryParse(item?.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                    ? parsed
                    : throw new ApplicationException($"Número de item inválido: {item}"),
            });
        }

        return result;
    }

    public static List<decimal> Quantities(ODataActionParameters parameters)
    {
        if (!parameters.TryGetValue("Quantities", out var value) || value is not IEnumerable sequence)
            return [];

        var result = new List<decimal>();

        foreach (var item in sequence)
        {
            result.Add(item switch
            {
                double d => (decimal)d,
                decimal m => m,
                int i => i,
                long l => l,
                float f => (decimal)f,
                _ => decimal.TryParse(item?.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed)
                    ? parsed
                    : throw new ApplicationException($"Quantidade inválida: {item}"),
            });
        }

        return result;
    }
}
