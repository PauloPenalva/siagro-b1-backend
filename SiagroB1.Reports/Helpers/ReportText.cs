using System.Globalization;
using SiagroB1.Domain.Entities;

namespace SiagroB1.Reports.Helpers;

/// <summary>
/// Textos pt-BR comuns a todos os relatórios em lista (documentos fiscais e logística). Tudo sai
/// de snapshots gravados nos documentos e de tabelas locais — nunca de ITEMS/BUSINESS_PARTNERS/
/// WAREHOUSES, vazias em modo SAPB1.
/// </summary>
public static class ReportText
{
    public const string NoProduct = "Sem produto vinculado";
    public const string InvertedPeriod = "A data final não pode ser anterior à inicial.";

    private static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("pt-BR");

    /// <summary>Período obrigatório e não invertido; devolve a mensagem do 400 ou null.</summary>
    public static string? ValidatePeriod(DateTime from, DateTime to, string missingMessage)
    {
        if (from == default || to == default)
            return missingMessage;

        if (to.Date < from.Date)
            return InvertedPeriod;

        return null;
    }

    /// <summary>"Rótulo: dd/MM/yyyy a dd/MM/yyyy" — primeira parte da linha de filtros.</summary>
    public static string Period(string label, DateTime from, DateTime to) =>
        $"{label}: {Date(from)} a {Date(to)}";

    public static string Date(DateTime? value) =>
        value is { } date ? date.ToString("dd/MM/yyyy", Culture) : "";

    /// <summary>"número/série"; sem número não há o que imprimir, nem a barra.</summary>
    public static string DocumentNumber(string? number, string? series)
    {
        if (string.IsNullOrWhiteSpace(number))
            return "";

        return string.IsNullOrWhiteSpace(series) ? number.Trim() : $"{number.Trim()}/{series.Trim()}";
    }

    public static string Partner(string? code, string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return code ?? "";

        return string.IsNullOrWhiteSpace(code) ? name : $"({code}) {name}";
    }

    public static string Product(string? code, string? name)
    {
        if (string.IsNullOrWhiteSpace(code))
            return string.IsNullOrWhiteSpace(name) ? NoProduct : name;

        return string.IsNullOrWhiteSpace(name) ? code : $"{name} ({code})";
    }

    public static string BranchName(Branch? branch, string? code)
    {
        var name = branch?.ShortName;
        if (string.IsNullOrWhiteSpace(name))
            name = branch?.BranchName;

        return string.IsNullOrWhiteSpace(name) ? code ?? "" : name;
    }

    /// <summary>Nome do snapshot; sem nome, o código; sem nenhum, vazio.</summary>
    public static string NameOrCode(string? code, string? name) =>
        string.IsNullOrWhiteSpace(name) ? (code ?? "").Trim() : name.Trim();

    /// <summary>Valores distintos e não vazios, em ordem ORDINAL (estável em qualquer cultura), separados por ", ".</summary>
    public static string JoinDistinct(IEnumerable<string?> values) =>
        string.Join(", ", values
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v!.Trim())
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal));

    public static string Describe(string? description, string fallback) =>
        string.IsNullOrWhiteSpace(description) ? fallback : description;

    public static string JoinFilters(IEnumerable<string> parts) => string.Join(" | ", parts);

    /// <summary>
    /// Verdadeiro quando todas as linhas têm a mesma UM (ou não há linha). Só então o total geral
    /// de quantidade faz sentido — KG e TN nunca se somam.
    /// </summary>
    public static bool IsSingleUnit(IEnumerable<string?> units) =>
        units.Select(u => (u ?? "").Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() <= 1;

    /// <summary>Produto de um grupo "Produto (código) - UM", sem o sufixo da UM.</summary>
    public static string? ProductOfGroup(string? group)
    {
        if (group is null)
            return null;

        var cut = group.LastIndexOf(" - ", StringComparison.Ordinal);
        return cut > 0 ? group[..cut] : group;
    }
}
