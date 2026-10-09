using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Reports.Dtos;

namespace SiagroB1.Reports.Helpers;

/// <summary>
/// Textos pt-BR dos relatórios de documentos fiscais. O que não é fiscal (data, parceiro,
/// produto, filial, período) mora em <see cref="ReportText"/>, compartilhado com a logística;
/// aqui ficam os delegadores — mantidos para não mexer nos serviços do pacote 1.
/// </summary>
public static class InvoiceReportText
{
    public const string NoOrigin = "—";
    public const string NoProduct = ReportText.NoProduct;

    private static readonly InvoiceStatus[] DefaultStatuses =
        [InvoiceStatus.Pending, InvoiceStatus.Confirmed, InvoiceStatus.Returned];

    public static string? Validate(InvoiceReportRequest request) =>
        ReportText.ValidatePeriod(request.FromDate, request.ToDate, "Informe o período de emissão.");

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

    public static string DocumentNumber(string? number, string? series) => ReportText.DocumentNumber(number, series);

    public static string Partner(string? code, string? name) => ReportText.Partner(code, name);

    public static string Product(string? code, string? name) => ReportText.Product(code, name);

    public static string BranchName(Branch? branch, string? code) => ReportText.BranchName(branch, code);

    public static string Date(DateTime? value) => ReportText.Date(value);

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
        var parts = new List<string> { ReportText.Period("Emissão", request.FromDate, request.ToDate) };

        if (!string.IsNullOrWhiteSpace(request.BranchCode))
            parts.Add($"Filial: {ReportText.Describe(branchName, request.BranchCode)}");

        if (!string.IsNullOrWhiteSpace(request.CardCode))
            parts.Add($"{partnerLabel}: {ReportText.Describe(partnerName, request.CardCode)}");

        if (!string.IsNullOrWhiteSpace(request.ItemCode))
            parts.Add($"Produto: {ReportText.Describe(productName, request.ItemCode)}");

        parts.Add("Situação: " + string.Join(", ", EffectiveStatuses(request.Statuses).Select(s => Status(s))));

        if (standalone && request.NfeStatuses is { Count: > 0 } nfe)
            parts.Add("Situação NF-e: " + string.Join(", ", nfe.Distinct().Select(s => s == NfeStatus.None ? "Não emitida" : Nfe(s))));

        parts.AddRange(extra);

        return ReportText.JoinFilters(parts);
    }
}
