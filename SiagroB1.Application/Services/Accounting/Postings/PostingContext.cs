namespace SiagroB1.Application.Services.Accounting.Postings;

/// <summary>
/// Uma "linha" do contexto: um item do documento (LP por item) ou o próprio documento (LP por total). As chaves de
/// <see cref="Accounts"/> e <see cref="Values"/> são as fontes do catálogo (ex.: "Item.LedgerAccount", "Line.Total").
/// </summary>
public sealed record PostingRow(
    IReadOnlyDictionary<string, string?> Accounts,
    IReadOnlyDictionary<string, decimal> Values,
    string? CostCenterCode,
    string? AccountingItemCode,
    string? DistributionRuleCode,
    IReadOnlyDictionary<string, string> Placeholders);

/// <summary>
/// O evento a contabilizar, já traduzido do módulo de origem para o formato neutro do motor. A chave
/// (<see cref="DocumentType"/>, <see cref="DocumentKey"/>, <see cref="Event"/>) é o rastro do lançamento.
/// <see cref="ExternalEvent"/> marca o que não pode ser recusado (cancelamento de NF-e homologado pela SEFAZ).
/// </summary>
public sealed record PostingContext(
    string DocumentType,
    string DocumentKey,
    string Event,
    DateTime Date,
    string BranchCode,
    string? PartnerCode,
    IReadOnlyList<PostingRow> Rows,
    bool ExternalEvent = false);
