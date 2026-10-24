using SiagroB1.Domain.Entities;

namespace SiagroB1.Reports.Dtos;

/// <summary>Pedido da ECD: qualquer filial da empresa (CNPJ raiz) e o ano civil.</summary>
public class EcdRequest
{
    public string BranchCode { get; set; } = "";
    public int Year { get; set; }

    /// <summary>0 = original, 1 = substituta.</summary>
    public int Purpose { get; set; }

    /// <summary>Hash do livro substituído (COD_HASH_SUB do 0000), obrigatório na substituta.</summary>
    public string? ReplacedBookHash { get; set; }
}

/// <summary>Tudo o que a ECD do ano precisa: escrituração centralizada de todas as filiais da raiz.</summary>
public class EcdData
{
    /// <summary>Matriz (/0001) da raiz; nula quando não cadastrada.</summary>
    public Branch? Declarant { get; set; }

    public List<Branch> Branches { get; set; } = [];

    /// <summary>Nome do município da declarante (DESC_MUN do I030).</summary>
    public string? MunicipalityName { get; set; }

    /// <summary>Parâmetros da ECD da declarante.</summary>
    public EcdSettings? Settings { get; set; }

    /// <summary>Contabilista: parâmetros do SPED Fiscal da declarante.</summary>
    public SpedFiscalSettings? Accountant { get; set; }

    public AccountingSettings Accounting { get; set; } = new();

    public List<LedgerAccount> Accounts { get; set; } = [];
    public List<CostCenter> CostCenters { get; set; } = [];

    /// <summary>Lançamentos do ano (com as linhas) de todas as filiais da raiz, em ordem de data e DocEntry.</summary>
    public List<JournalEntry> Entries { get; set; } = [];

    /// <summary>Saldo em 31/12 do ano anterior por conta analítica e centro de custo (+ devedor); só os não zerados.</summary>
    public Dictionary<(string Account, string? CostCenter), decimal> OpeningBalances { get; set; } = [];

    public List<AccountingPeriod> Periods { get; set; } = [];
    public ManagementView? BalanceSheet { get; set; }
    public ManagementView? IncomeStatement { get; set; }
}

/// <summary>Inconsistência da ECD: bloqueante (recusa o arquivo) ou aviso.</summary>
public class EcdIssueDto
{
    public bool Blocking { get; set; }
    public string Message { get; set; } = "";
    public string Reference { get; set; } = "";
}

/// <summary>Totais de conferência do ano: débitos = créditos e resultado do exercício pela DRE.</summary>
public class EcdTotalsDto
{
    public decimal TotalDebit { get; set; }
    public decimal TotalCredit { get; set; }

    /// <summary>Lucro positivo, prejuízo negativo (sem a apuração de resultado).</summary>
    public decimal NetIncome { get; set; }
}

/// <summary>Prévia antes de gerar: inconsistências e totais.</summary>
public class EcdCheckDto
{
    public List<EcdIssueDto> Issues { get; set; } = [];
    public EcdTotalsDto Totals { get; set; } = new();
}
