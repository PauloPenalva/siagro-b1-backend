using System.Text.RegularExpressions;

namespace SiagroB1.Application.Services.SalesInvoices;

/// <summary>
/// Lê a lista de documentos de origem gravada no <c>Comments</c> da entrada de armazém de uma
/// recusa de carga ("... Documento(s): 000009637, 000009638. Cliente(s): ...", escrita por
/// <c>ShipmentLoadsRefuseService.ReturnToWarehouseAsync</c>).
/// </summary>
/// <remarks>
/// É o único vínculo persistido entre a entrada e os documentos da recusa que a criou. Devolve
/// <c>null</c> quando o texto não tem a lista — quem chama cai então na regra por carga.
/// </remarks>
internal static partial class RefusalEntryDocuments
{
    [GeneratedRegex(@"Documento\(s\):\s*([^.]*)\.")]
    private static partial Regex DocumentsPattern();

    public static HashSet<string>? Parse(string? comments)
    {
        if (string.IsNullOrWhiteSpace(comments))
            return null;

        var match = DocumentsPattern().Match(comments);
        if (!match.Success)
            return null;

        var numbers = match.Groups[1].Value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.Ordinal);

        return numbers.Count == 0 ? null : numbers;
    }
}
