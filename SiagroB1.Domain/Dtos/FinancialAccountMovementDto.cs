using System.Text.Json.Serialization;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Domain.Dtos;

/// <summary>Linha de movimento da conta financeira como a tela e a reconciliação a recebem.</summary>
public class FinancialAccountMovementDto
{
    [JsonPropertyName("Source")] public FinancialAccountMovementSource Source { get; set; }
    [JsonPropertyName("Key")] public Guid Key { get; set; }
    [JsonPropertyName("Date")] public DateTime Date { get; set; }
    /// <summary>Sinal de caixa: positivo entra, negativo sai.</summary>
    [JsonPropertyName("Amount")] public decimal Amount { get; set; }
    [JsonPropertyName("Description")] public string Description { get; set; } = string.Empty;
    [JsonPropertyName("DocumentReference")] public string? DocumentReference { get; set; }
    [JsonPropertyName("CardCode")] public string? CardCode { get; set; }
    [JsonPropertyName("CardName")] public string? CardName { get; set; }
    [JsonPropertyName("FinancialDocumentKey")] public Guid? FinancialDocumentKey { get; set; }
    [JsonPropertyName("SettlementOrigin")] public FinancialSettlementOrigin? SettlementOrigin { get; set; }
    [JsonPropertyName("ReconciliationKey")] public Guid? ReconciliationKey { get; set; }
    [JsonPropertyName("Reconciled")] public bool Reconciled { get; set; }
    /// <summary>Marcada na reconciliação consultada (só em GetLines).</summary>
    [JsonPropertyName("Marked")] public bool Marked { get; set; }
    /// <summary>Saldo acumulado após a linha (só em GetMovements).</summary>
    [JsonPropertyName("RunningBalance")] public decimal RunningBalance { get; set; }
}

public class FinancialAccountReconciliationLinesDto
{
    [JsonPropertyName("OpeningBalance")] public decimal OpeningBalance { get; set; }
    [JsonPropertyName("StatementBalance")] public decimal StatementBalance { get; set; }
    /// <summary>Soma das linhas marcadas nesta reconciliação.</summary>
    [JsonPropertyName("ReconciledAmount")] public decimal ReconciledAmount { get; set; }
    /// <summary>(Abertura + marcado) − extrato. Zero libera o fechamento.</summary>
    [JsonPropertyName("Difference")] public decimal Difference { get; set; }
    [JsonPropertyName("Rows")] public List<FinancialAccountMovementDto> Rows { get; set; } = [];
}

public class FinancialAccountMovementsDto
{
    [JsonPropertyName("OpeningBalance")] public decimal OpeningBalance { get; set; }
    [JsonPropertyName("ClosingBalance")] public decimal ClosingBalance { get; set; }
    [JsonPropertyName("TotalIn")] public decimal TotalIn { get; set; }
    [JsonPropertyName("TotalOut")] public decimal TotalOut { get; set; }
    [JsonPropertyName("Rows")] public List<FinancialAccountMovementDto> Rows { get; set; } = [];
}
