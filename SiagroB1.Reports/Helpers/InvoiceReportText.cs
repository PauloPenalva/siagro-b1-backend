using System.Globalization;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Reports.Dtos;

namespace SiagroB1.Reports.Helpers;

/// <summary>
/// Textos pt-BR comuns aos relatórios de documentos fiscais. Tudo sai do snapshot gravado
/// no documento — nunca de ITEMS/BUSINESS_PARTNERS, vazias em modo SAPB1.
/// </summary>
public static class InvoiceReportText
{
    public const string NoOrigin = "—";
    public const string NoProduct = "Sem produto vinculado";

    private static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("pt-BR");

    private static readonly InvoiceStatus[] DefaultStatuses =
        [InvoiceStatus.Pending, InvoiceStatus.Confirmed, InvoiceStatus.Returned];

    public static string? Validate(InvoiceReportRequest request)
    {
        if (request.FromDate == default || request.ToDate == default)
            return "Informe o período de emissão.";

        if (request.ToDate.Date < request.FromDate.Date)
            return "A data final não pode ser anterior à inicial.";

        return null;
    }

    /// <summary>Array (e não lista) para o EF traduzir o Contains em IN.</summary>
    public static InvoiceStatus[] EffectiveStatuses(IReadOnlyCollection<InvoiceStatus>? requested) =>
        requested is { Count: > 0 } ? requested.Distinct().ToArray() : DefaultStatuses;

    public static string Status(InvoiceStatus? status) => status switch
    {
        InvoiceStatus.Confirmed => "Confirmado",
        InvoiceStatus.Cancelled => "Cancelado",
        InvoiceStatus.Returned => "Retornado",
        _ => "Pendente",
    };

    public static string Nfe(NfeStatus status) => status switch
    {
        NfeStatus.Processing => "Em processamento",
        NfeStatus.Authorized => "Autorizada",
        NfeStatus.Rejected => "Rejeitada",
        NfeStatus.Denied => "Denegada",
        NfeStatus.Cancelled => "Cancelada",
        NfeStatus.Voided => "Inutilizada",
        _ => "",
    };

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

    public static string Date(DateTime? value) =>
        value is { } date ? date.ToString("dd/MM/yyyy", Culture) : "";

    /// <summary>
    /// Linha de filtros do cabeçalho. Descrições (nome do parceiro, produto, filial) vêm das
    /// linhas do resultado; sem resultado, resta o código.
    /// </summary>
    public static string BuildFilters(
        InvoiceReportRequest request,
        bool standalone,
        string partnerLabel,
        string? partnerName,
        string? productName,
        string? branchName,
        IEnumerable<string> extra)
    {
        var parts = new List<string> { $"Emissão: {Date(request.FromDate)} a {Date(request.ToDate)}" };

        if (!string.IsNullOrWhiteSpace(request.BranchCode))
            parts.Add($"Filial: {Describe(branchName, request.BranchCode)}");

        if (!string.IsNullOrWhiteSpace(request.CardCode))
            parts.Add($"{partnerLabel}: {Describe(partnerName, request.CardCode)}");

        if (!string.IsNullOrWhiteSpace(request.ItemCode))
            parts.Add($"Produto: {Describe(productName, request.ItemCode)}");

        parts.Add("Situação: " + string.Join(", ", EffectiveStatuses(request.Statuses).Select(s => Status(s))));

        if (standalone && request.NfeStatuses is { Count: > 0 } nfe)
            parts.Add("Situação NF-e: " + string.Join(", ", nfe.Distinct().Select(s => s == NfeStatus.None ? "Não emitida" : Nfe(s))));

        parts.AddRange(extra);

        return string.Join(" | ", parts);
    }

    private static string Describe(string? description, string fallback) =>
        string.IsNullOrWhiteSpace(description) ? fallback : description;
}
