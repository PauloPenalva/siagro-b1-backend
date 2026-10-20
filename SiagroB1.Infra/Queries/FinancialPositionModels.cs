using SiagroB1.Domain.Entities;

namespace SiagroB1.Infra.Queries;

/// <summary>Quais títulos entram na posição (spec R4): nunca os dois juntos, para não contar o mesmo dinheiro duas vezes.</summary>
public enum FinancialPositionMode { Firm, Provisional }

/// <param name="Documents">Já filtrados por direção/parceiro; podem incluir cancelados.</param>
/// <param name="Settlements">Todas as linhas do ledger desses documentos.</param>
/// <param name="Compositions">Composições vigentes que envolvem esses documentos.</param>
public sealed record FinancialPositionInput(
    IReadOnlyList<FinancialDocument> Documents,
    IReadOnlyList<FinancialSettlement> Settlements,
    IReadOnlyList<FinancialDocumentComposition> Compositions);

/// <summary>Amount: NetAmount, negativo para devolução de contrato (R7).</summary>
public sealed record PositionTitle(FinancialDocument Document, decimal Amount, decimal SettledAtBase, decimal OpenAtBase);

public sealed record EffectiveSettlement(FinancialDocument Document, FinancialSettlement Source, DateTime Date,
    decimal Amount, decimal Interest, decimal Fine, decimal Discount, bool IsDistributed, bool IsReturnNetting = false);

public sealed record FinancialPosition(IReadOnlyList<PositionTitle> Titles, IReadOnlyList<EffectiveSettlement> Settlements);
