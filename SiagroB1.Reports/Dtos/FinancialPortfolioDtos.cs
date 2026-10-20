using SiagroB1.Domain.Enums;
using SiagroB1.Infra.Queries;

namespace SiagroB1.Reports.Dtos;

/// <summary>Carteira de Contas a Receber / a Pagar: o fim do período de vencimento é a data-base (spec R1/R2).</summary>
public class FinancialPortfolioRequest
{
    public FinancialDirection Direction { get; set; }
    public DateTime DueDateFrom { get; set; }
    public DateTime DueDateTo { get; set; }
    public FinancialPositionMode Mode { get; set; }
    public string? CardCode { get; set; }
}

/// <summary>Linha da carteira como o template FinancialPortfolio.frx lê.</summary>
public class FinancialPortfolioRowDto
{
    /// <summary>Quebra do relatório: parceiro + moeda (o subtotal nunca soma moedas diferentes).</summary>
    public string GroupKey { get; set; } = string.Empty;
    public string Partner { get; set; } = string.Empty;
    public string Document { get; set; } = string.Empty;
    public string NfeNumber { get; set; } = string.Empty;
    public string NfeSeries { get; set; } = string.Empty;
    public string Origin { get; set; } = string.Empty;
    public string DocumentDate { get; set; } = string.Empty;
    public string DueDate { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public decimal Settled { get; set; }
    public decimal Open { get; set; }
    public int DaysOverdue { get; set; }
}
