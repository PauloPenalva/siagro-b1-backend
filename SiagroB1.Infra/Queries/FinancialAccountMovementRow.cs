using SiagroB1.Domain.Enums;

namespace SiagroB1.Infra.Queries;

/// <summary>
/// Uma linha do movimento da conta financeira: baixa de título ou lançamento manual, com o valor no SINAL DE CAIXA
/// (positivo entra, negativo sai). Classe com setters (não record posicional) para o EF traduzir o Concat.
/// </summary>
public class FinancialAccountMovementRow
{
    public FinancialAccountMovementSource Source { get; set; }
    public Guid Key { get; set; }
    public DateTime Date { get; set; }
    public DateTime? CreatedAt { get; set; }
    public decimal Amount { get; set; }
    public string Description { get; set; } = string.Empty;
    public string? DocumentReference { get; set; }
    public string? CardCode { get; set; }
    public string? CardName { get; set; }
    public Guid? FinancialDocumentKey { get; set; }
    public FinancialSettlementOrigin? SettlementOrigin { get; set; }
    public Guid? ReconciliationKey { get; set; }
}
