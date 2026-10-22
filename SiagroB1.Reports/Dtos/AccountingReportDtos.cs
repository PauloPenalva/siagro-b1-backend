namespace SiagroB1.Reports.Dtos;

public class IncomeStatementReportRequest
{
    public DateTime DateFrom { get; set; }
    public DateTime DateTo { get; set; }
    public string ViewCode { get; set; } = string.Empty;
    public string? BranchCode { get; set; }

    /// <summary>Colunas por centro de custo (com "Sem centro"), além do total.</summary>
    public bool ByCostCenter { get; set; }
}

/// <summary>Linha da DRE como o template AccountingIncomeStatement.frx lê.</summary>
public class IncomeStatementRowDto
{
    public string Code { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int Level { get; set; }
    public bool Bold { get; set; }
    public decimal Period { get; set; }
    public decimal YearToDate { get; set; }

    /// <summary>% do valor do período sobre a primeira linha da visão (a receita); nulo se ela for zero.</summary>
    public decimal? PercentOfRevenue { get; set; }

    public Dictionary<string, decimal>? ByCostCenter { get; set; }
}

public class BalanceSheetReportRequest
{
    public DateTime Date { get; set; }
    public DateTime? CompareDate { get; set; }
    public string ViewCode { get; set; } = string.Empty;
    public string? BranchCode { get; set; }
}

/// <summary>Linha do Balanço como o template AccountingBalanceSheet.frx lê.</summary>
public class BalanceSheetRowDto
{
    public string Code { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int Level { get; set; }
    public bool Bold { get; set; }
    public decimal Balance { get; set; }
    public decimal? CompareBalance { get; set; }
}

public class JournalReportRequest
{
    public DateTime DateFrom { get; set; }
    public DateTime DateTo { get; set; }
    public string? BranchCode { get; set; }

    /// <summary>Número da primeira folha (o livro continua a numeração do anterior).</summary>
    public int StartPage { get; set; } = 1;

    public int BookNumber { get; set; } = 1;
}

/// <summary>Linha do Diário como o template AccountingJournal.frx lê.</summary>
public class JournalRowDto
{
    public DateTime Date { get; set; }
    public string Batch { get; set; } = string.Empty;
    public string SubBatch { get; set; } = string.Empty;
    public string DocumentNumber { get; set; } = string.Empty;
    public int LineNumber { get; set; }
    public string BranchCode { get; set; } = string.Empty;
    public string Account { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;
    public decimal Debit { get; set; }
    public decimal Credit { get; set; }
    public string History { get; set; } = string.Empty;
    public string CostCenter { get; set; } = string.Empty;
    public string Item { get; set; } = string.Empty;
}

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
