using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Reports.Dtos;
using SiagroB1.Reports.Services.Ecd;

namespace SiagroB1.Reports.Services.Ecf;

/// <summary>Período de apuração da ECF: A00 (ano) ou T01..T04; Number é o Period do ajuste (0 anual, 1..4).</summary>
public sealed record EcfPeriod(string Code, int Number, DateTime Start, DateTime End, int Months);

/// <summary>Apuração de um período: lucro antes do IR (soma do K355), ajustes de cada tributo e o cálculo.</summary>
public sealed record EcfPeriodAssessment(
    EcfPeriod Period,
    decimal BookProfit,
    List<EcfAdjustment> IrpjAdjustments,
    List<EcfAdjustment> CsllAdjustments,
    IncomeTaxResult Irpj,
    IncomeTaxResult Csll)
{
    public decimal IrpjAdditions => Sum(IrpjAdjustments, EcfAdjustmentKind.Addition);
    public decimal IrpjExclusions => Sum(IrpjAdjustments, EcfAdjustmentKind.Exclusion);
    public decimal CsllAdditions => Sum(CsllAdjustments, EcfAdjustmentKind.Addition);
    public decimal CsllExclusions => Sum(CsllAdjustments, EcfAdjustmentKind.Exclusion);

    internal static decimal Sum(IEnumerable<EcfAdjustment> list, EcfAdjustmentKind kind) =>
        list.Where(a => a.Kind == kind).Sum(a => a.Amount);
}

/// <summary>
/// Estimativa do mês (anual, balancete de suspensão/redução): imposto sobre o lucro acumulado de janeiro até o mês
/// (adicional sobre 20.000,00 × meses) menos o já devido nos meses anteriores, nunca negativo.
/// </summary>
public sealed record EcfMonthlyEstimate(
    int Month,
    decimal CumulativeProfit,
    IncomeTaxResult IrpjCumulative,
    decimal IrpjPreviouslyDue,
    decimal Irpj,
    IncomeTaxResult CsllCumulative,
    decimal CsllPreviouslyDue,
    decimal Csll);

/// <summary>
/// Apuração do Lucro Real da ECF, a mesma para o arquivo e para a tela de Verificar. Pura (sem banco).
/// Lucro antes do IR do período = resultado das contas de receita e despesa no período sem a apuração de resultado
/// (= soma do K355 do período; o período que começa em 1º de janeiro leva também o saldo de abertura delas, como o
/// I355 da ECD).
/// </summary>
public static class EcfAssessment
{
    public static List<EcfPeriod> Periods(int year, EcfAssessmentForm form) => form == EcfAssessmentForm.Quarterly
        ? Enumerable.Range(1, 4).Select(q => new EcfPeriod($"T{q:00}", q, new DateTime(year, q * 3 - 2, 1),
            new DateTime(year, q * 3, 1).AddMonths(1).AddDays(-1), 3)).ToList()
        : [new EcfPeriod("A00", 0, new DateTime(year, 1, 1), new DateTime(year, 12, 31), 12)];

    /// <summary>
    /// Períodos dos blocos K e L: no anual com balancete de suspensão/redução (MES_BAL_RED "B" nos 12 meses), um
    /// período A01..A11 com o acumulado de janeiro até o mês e o A00 do ano (dezembro = A00); no trimestral, T01..T04.
    /// </summary>
    public static List<EcfPeriod> BalancePeriods(int year, EcfAssessmentForm form) => form == EcfAssessmentForm.Quarterly
        ? Periods(year, form)
        : Enumerable.Range(1, 11).Select(m => new EcfPeriod($"A{m:00}", 0, new DateTime(year, 1, 1),
                new DateTime(year, m, 1).AddMonths(1).AddDays(-1), m))
            .Concat(Periods(year, form)).ToList();

    /// <summary>Saldo das contas de resultado antes da apuração no período (linhas do K355).</summary>
    internal static List<KeyValuePair<(string Account, string? CostCenter), decimal>> ResultBalances(EcdData data,
        List<EcdPosting> postings, DateTime start, DateTime end) =>
        EcdLedger.ResultBalances(data,
            postings.Where(p => !p.ResultClosing && p.Entry.PostingDate.Date >= start && p.Entry.PostingDate.Date <= end),
            includeOpening: start is { Month: 1, Day: 1 });

    /// <summary>Lucro (positivo) ou prejuízo (negativo) antes do IR: soma do K355 com o sinal invertido.</summary>
    public static decimal BookProfit(EcdData data, List<EcdPosting> postings, DateTime start, DateTime end) =>
        -ResultBalances(data, postings, start, end).Sum(kv => kv.Value);

    public static List<EcfPeriodAssessment> Assess(EcfData data, EcfAssessmentForm form, int year)
    {
        var postings = EcdLedger.Postings(data.Ledger);
        return Periods(year, form).Select(period =>
        {
            var profit = BookProfit(data.Ledger, postings, period.Start, period.End);
            var mine = data.Adjustments.Where(a => a.Period == period.Number).ToList();
            // Ajuste "ambos" entra no M300 (IRPJ) e no M350 (CSLL)
            var irpj = mine.Where(a => a.Tax is EcfAdjustmentTax.Irpj or EcfAdjustmentTax.Both).ToList();
            var csll = mine.Where(a => a.Tax is EcfAdjustmentTax.Csll or EcfAdjustmentTax.Both).ToList();
            return new EcfPeriodAssessment(period, profit, irpj, csll,
                IncomeTaxCalculator.ComputeIrpj(profit, EcfPeriodAssessment.Sum(irpj, EcfAdjustmentKind.Addition),
                    EcfPeriodAssessment.Sum(irpj, EcfAdjustmentKind.Exclusion),
                    EcfPeriodAssessment.Sum(irpj, EcfAdjustmentKind.LossOffset), period.Months),
                IncomeTaxCalculator.ComputeCsll(profit, EcfPeriodAssessment.Sum(csll, EcfAdjustmentKind.Addition),
                    EcfPeriodAssessment.Sum(csll, EcfAdjustmentKind.Exclusion),
                    EcfPeriodAssessment.Sum(csll, EcfAdjustmentKind.LossOffset)));
        }).ToList();
    }

    /// <summary>
    /// Estimativas mensais da apuração anual pelo balancete acumulado de janeiro até o mês. Os ajustes do LALUR/LACS
    /// cadastrados no período anual (0) valem só para o ano (M300/M350, N630/N670): os balancetes mensais saem sem
    /// ajustes.
    /// </summary>
    public static List<EcfMonthlyEstimate> MonthlyEstimates(EcfData data, int year)
    {
        var postings = EcdLedger.Postings(data.Ledger);
        var start = new DateTime(year, 1, 1);
        var result = new List<EcfMonthlyEstimate>();
        decimal irpjDue = 0m, csllDue = 0m;
        for (var month = 1; month <= 12; month++)
        {
            var end = new DateTime(year, month, 1).AddMonths(1).AddDays(-1);
            var profit = BookProfit(data.Ledger, postings, start, end);
            var irpj = IncomeTaxCalculator.ComputeIrpj(profit, 0m, 0m, 0m, month);
            var csll = IncomeTaxCalculator.ComputeCsll(profit, 0m, 0m, 0m);
            var irpjMonth = Math.Max(0m, irpj.Total - irpjDue);
            var csllMonth = Math.Max(0m, csll.Total - csllDue);
            result.Add(new EcfMonthlyEstimate(month, profit, irpj, irpjDue, irpjMonth, csll, csllDue, csllMonth));
            irpjDue += irpjMonth;
            csllDue += csllMonth;
        }

        return result;
    }
}
