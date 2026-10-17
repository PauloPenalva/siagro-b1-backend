using System.Collections;
using System.Globalization;
using Microsoft.AspNetCore.OData.Formatter;

namespace SiagroB1.Web.Actions.Nfe;

/// <summary>Leitura compartilhada dos parâmetros das actions "Devolver" (venda e compra).</summary>
public static class NfeReturnActionParameters
{
    /// <summary>A série da emissão; nulo quando falta ou não é um inteiro (o OData entrega int, long, double ou texto).</summary>
    public static int? Series(ODataActionParameters parameters)
    {
        if (!parameters.TryGetValue("Series", out var value) || value is null)
            return null;

        return value switch
        {
            int i => i,
            long l when l is >= int.MinValue and <= int.MaxValue => (int)l,
            double d when d == Math.Truncate(d) && d is >= int.MinValue and <= int.MaxValue => (int)d,
            string text when int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => null,
        };
    }

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
