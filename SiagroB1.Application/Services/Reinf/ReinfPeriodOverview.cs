using SiagroB1.Domain.Enums;

namespace SiagroB1.Application.Services.Reinf;

/// <summary>Visão do período: linhas apuradas × eventos já transmitidos, mais as pendências.</summary>
public sealed record ReinfPeriodOverview(
    string TaxIdRoot, string Period, ReinfPeriodStatus PeriodStatus,
    IReadOnlyList<ReinfOverviewRow> Rows, IReadOnlyList<ReinfIssue> Issues);

/// <summary>
/// Situação: "Novo", "Enviado", "Aceito", "Rejeitado", "Desatualizado", "Excluir" ou "Excluído".
/// <c>Row</c> nulo = produtor com evento aceito que sumiu da apuração (precisa de R-9000).
/// </summary>
public sealed record ReinfOverviewRow(
    ReinfAcquisitionRow? Row, Guid? EventKey, string Situation, string? Receipt, string? Occurrences);
