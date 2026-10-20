using SiagroB1.Domain.Enums;
using SiagroB1.Infra.Queries;

namespace SiagroB1.Reports.Dtos;

/// <summary>Baixas de Recebíveis / Pagáveis no período [DateFrom, DateTo]; DateTo é a data-base da posição.</summary>
public class FinancialSettlementsRequest
{
    public FinancialDirection Direction { get; set; }
    public DateTime DateFrom { get; set; }
    public DateTime DateTo { get; set; }
    public FinancialPositionMode Mode { get; set; }
    public string? CardCode { get; set; }
    public string? AccountCode { get; set; }
}

/// <summary>Linha de baixa como o template FinancialSettlements.frx lê.</summary>
public class FinancialSettlementRowDto
{
    /// <summary>Quebra do relatório: data da baixa + moeda (o total do dia nunca soma moedas diferentes).</summary>
    public string GroupKey { get; set; } = string.Empty;
    public string Date { get; set; } = string.Empty;
    public string Partner { get; set; } = string.Empty;
    public string Document { get; set; } = string.Empty;
    public string NfeNumber { get; set; } = string.Empty;
    public string NfeSeries { get; set; } = string.Empty;
    public string Account { get; set; } = string.Empty;
    public string Method { get; set; } = string.Empty;
    public string Currency { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public decimal Interest { get; set; }
    public decimal Fine { get; set; }
    public decimal Discount { get; set; }
}
