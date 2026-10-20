using SiagroB1.Domain.Enums;
using SiagroB1.Infra.Queries;

namespace SiagroB1.Reports.Dtos;

/// <summary>Extrato do parceiro em [DateFrom, DateTo]. Direction nulo: a receber e a pagar juntos.</summary>
public class PartnerStatementRequest
{
    public string? CardCode { get; set; }
    public DateTime DateFrom { get; set; }
    public DateTime DateTo { get; set; }
    public FinancialDirection? Direction { get; set; }
    public FinancialPositionMode Mode { get; set; }
}

/// <summary>Linha do extrato como o template PartnerStatement.frx lê. Saldo = débitos − créditos (C2).</summary>
public class PartnerStatementRowDto
{
    /// <summary>Quebra do relatório: moeda (o saldo nunca soma moedas diferentes).</summary>
    public string Currency { get; set; } = string.Empty;
    public string Date { get; set; } = string.Empty;
    public string History { get; set; } = string.Empty;
    public string Document { get; set; } = string.Empty;
    public string NfeNumber { get; set; } = string.Empty;
    public string NfeSeries { get; set; } = string.Empty;
    public decimal Debit { get; set; }
    public decimal Credit { get; set; }
    public decimal Balance { get; set; }
    /// <summary>Saldo anterior da moeda da linha (repetido para o cabeçalho do grupo).</summary>
    public decimal OpeningBalance { get; set; }
    /// <summary>Saldo final da moeda da linha (posição em DateTo).</summary>
    public decimal ClosingBalance { get; set; }
}
