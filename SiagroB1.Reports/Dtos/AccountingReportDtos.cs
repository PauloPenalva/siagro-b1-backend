namespace SiagroB1.Reports.Dtos;

public class LedgerReportRequest
{
    public DateTime DateFrom { get; set; }
    public DateTime DateTo { get; set; }
    public string? BranchCode { get; set; }
    public string? AccountFrom { get; set; }
    public string? AccountTo { get; set; }
    public string? CostCenterCode { get; set; }
    public string? AccountingItemCode { get; set; }
}

/// <summary>Linha do Razão como o template AccountingLedger.frx lê (a primeira de cada conta é o saldo anterior).</summary>
public class LedgerReportRowDto
{
    public string AccountCode { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;
    public string Date { get; set; } = string.Empty;
    public string Document { get; set; } = string.Empty;
    public string BranchCode { get; set; } = string.Empty;
    public string History { get; set; } = string.Empty;
    public string Counterpart { get; set; } = string.Empty;
    public string CostCenterCode { get; set; } = string.Empty;
    public decimal Debit { get; set; }
    public decimal Credit { get; set; }
    public decimal Balance { get; set; }
    public string BalanceSide { get; set; } = string.Empty;
}

public class TrialBalanceReportRequest
{
    public DateTime DateFrom { get; set; }
    public DateTime DateTo { get; set; }
    public string? BranchCode { get; set; }
    public string? AccountFrom { get; set; }
    public string? AccountTo { get; set; }
    public string? CostCenterCode { get; set; }
    public string? AccountingItemCode { get; set; }

    /// <summary>Profundidade máxima na hierarquia (1 = contas sem superior). Vazio = todas.</summary>
    public int? Level { get; set; }

    /// <summary>Esconde contas sem débito nem crédito no período (mesmo com saldo).</summary>
    public bool OnlyWithMovement { get; set; }
}

/// <summary>Linha do Balancete como o template AccountingTrialBalance.frx lê.</summary>
public class TrialBalanceReportRowDto
{
    public string AccountCode { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;
    public int Level { get; set; }
    public bool Analytic { get; set; }
    public decimal Opening { get; set; }
    public string OpeningSide { get; set; } = string.Empty;
    public decimal Debit { get; set; }
    public decimal Credit { get; set; }
    public decimal Closing { get; set; }
    public string ClosingSide { get; set; } = string.Empty;
}
