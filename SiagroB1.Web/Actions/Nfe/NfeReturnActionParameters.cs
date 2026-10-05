using System.Collections;
using System.Globalization;
using Microsoft.AspNetCore.OData.Formatter;

namespace SiagroB1.Web.Actions.Nfe;

/// <summary>Leitura compartilhada dos parâmetros das actions "Devolver" (venda e compra).</summary>
public static class NfeReturnActionParameters
{
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
