namespace SiagroB1.Reports.Helpers;

/// <summary>
/// Agrupamento dos relatórios de itens: a chave é código do produto + UM. O nome exibido é o
/// mais frequente entre as linhas do mesmo código (empate: o primeiro em ordem alfabética);
/// sem nome algum, só o código. Linhas sem código agrupam pelo próprio nome. Não consulta
/// ITEMS (vazia em modo SAPB1).
/// </summary>
public static class InvoiceItemGrouping
{
    public static Func<T, string> BuildGroupResolver<T>(
        IEnumerable<T> lines,
        Func<T, string?> itemCode,
        Func<T, string?> itemName,
        Func<T, string?> unitOfMeasure)
    {
        var names = lines
            .Where(l => !string.IsNullOrWhiteSpace(itemCode(l)) && !string.IsNullOrWhiteSpace(itemName(l)))
            .GroupBy(l => itemCode(l)!.Trim())
            .ToDictionary(
                g => g.Key,
                g => g.GroupBy(l => itemName(l)!.Trim())
                    .OrderByDescending(n => n.Count())
                    .ThenBy(n => n.Key, StringComparer.CurrentCultureIgnoreCase)
                    .First().Key);

        return line =>
        {
            var code = itemCode(line)?.Trim();
            string product;
            if (string.IsNullOrWhiteSpace(code))
                product = InvoiceReportText.Product(null, itemName(line));
            else
                product = InvoiceReportText.Product(code, names.GetValueOrDefault(code));

            var uom = unitOfMeasure(line);
            return string.IsNullOrWhiteSpace(uom) ? product : $"{product} - {uom}";
        };
    }
}
