using SiagroB1.Fiscal.Reinf.Events;

namespace SiagroB1.Application.Services.Reinf;

/// <summary>Aquisição de produção rural apurada para um R-2055: filial (estabelecimento) × produtor no período.</summary>
public sealed record ReinfAcquisitionRow(
    string EstablishmentTaxId, string BranchCode, string CardCode, string CardName,
    int ProducerInscriptionType, string ProducerTaxId, bool PayrollOption,
    IReadOnlyList<R2055Detail> Details, decimal Gross, decimal Inss, decimal Rat, decimal Senar,
    string ContentHash);

/// <summary>Pendência encontrada na apuração (mensagem em pt-BR para o usuário).</summary>
public sealed record ReinfIssue(string Message, string? Reference);
