using SiagroB1.Domain.Entities;

namespace SiagroB1.Reports.Dtos;

/// <summary>Pedido da ECF: qualquer filial da empresa (CNPJ raiz) e o ano-calendário.</summary>
public class EcfRequest
{
    public string BranchCode { get; set; } = "";
    public int Year { get; set; }

    /// <summary>0 = original, 1 = retificadora (exige o hash da ECF anterior nos parâmetros da ECF).</summary>
    public int Purpose { get; set; }
}

/// <summary>Tudo o que a ECF do ano precisa: a carga da ECD (blocos J/K) mais parâmetros, sócios e ajustes.</summary>
public class EcfData
{
    public EcdData Ledger { get; set; } = new();
    public EcfSettings? Settings { get; set; }
    public List<EcfPartner> Partners { get; set; } = [];

    /// <summary>Ajustes do LALUR/LACS do ano, da declarante.</summary>
    public List<EcfAdjustment> Adjustments { get; set; } = [];
}

/// <summary>Totais de conferência de um período de apuração (A00 ou T01..T04), iguais aos do arquivo.</summary>
public class EcfPeriodTotalsDto
{
    /// <summary>Código do período na ECF: A00 (anual) ou T01..T04.</summary>
    public string Period { get; set; } = "";

    public DateTime Start { get; set; }
    public DateTime End { get; set; }

    /// <summary>Lucro antes do IRPJ/CSLL (resultado sem a apuração; prejuízo negativo).</summary>
    public decimal BookProfit { get; set; }

    public decimal IrpjAdditions { get; set; }
    public decimal IrpjExclusions { get; set; }
    public decimal IrpjLossOffset { get; set; }

    /// <summary>Lucro real (base do IRPJ).</summary>
    public decimal IrpjBase { get; set; }

    public decimal Irpj { get; set; }
    public decimal IrpjSurtax { get; set; }

    public decimal CsllAdditions { get; set; }
    public decimal CsllExclusions { get; set; }
    public decimal CsllLossOffset { get; set; }
    public decimal CsllBase { get; set; }
    public decimal Csll { get; set; }

    /// <summary>Anual: soma das estimativas mensais (N620/N660); trimestral: zero.</summary>
    public decimal IrpjEstimates { get; set; }

    public decimal CsllEstimates { get; set; }
}

/// <summary>Estimativa mensal (anual por balancete de suspensão/redução), N620/N660.</summary>
public class EcfMonthlyEstimateDto
{
    public int Month { get; set; }

    /// <summary>Lucro acumulado de janeiro até o mês.</summary>
    public decimal CumulativeProfit { get; set; }

    public decimal Irpj { get; set; }
    public decimal Csll { get; set; }
}

/// <summary>Prévia antes de gerar: inconsistências e totais por período.</summary>
public class EcfCheckDto
{
    public List<EcdIssueDto> Issues { get; set; } = [];
    public List<EcfPeriodTotalsDto> Periods { get; set; } = [];
    public List<EcfMonthlyEstimateDto> Estimates { get; set; } = [];
}
