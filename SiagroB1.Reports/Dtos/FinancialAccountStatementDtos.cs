namespace SiagroB1.Reports.Dtos;

public class FinancialAccountStatementRequest
{
    public string? AccountCode { get; set; }
    public DateTime DateFrom { get; set; }
    public DateTime DateTo { get; set; }
    public bool OnlyReconciled { get; set; }
}

/// <summary>Linha do extrato como o template FinancialAccountStatement.frx lê.</summary>
public class FinancialAccountStatementRowDto
{
    public string Date { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string DocumentReference { get; set; } = string.Empty;
    public string Partner { get; set; } = string.Empty;
    public decimal In { get; set; }
    public decimal Out { get; set; }
    public decimal Balance { get; set; }
    public string Reconciled { get; set; } = string.Empty;
}
