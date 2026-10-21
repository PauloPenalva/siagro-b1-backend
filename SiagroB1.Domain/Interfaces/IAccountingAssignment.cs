namespace SiagroB1.Domain.Interfaces;

/// <summary>
/// Amarração contábil de um documento (modelo SIGACTB do Protheus): centro de custo (CTT),
/// item contábil (CTD), rateio pré-configurado (CTJ) e conta contábil (CT1). Sem FK: o
/// cadastro é dual-mode e a validação é feita por serviço.
/// </summary>
public interface IAccountingAssignment
{
    string? CostCenterCode { get; set; }
    string? AccountingItemCode { get; set; }
    string? DistributionRuleCode { get; set; }
    string? LedgerAccountCode { get; set; }
}

/// <summary>Foto dos quatro campos de amarração contábil, para comparar antes/depois.</summary>
public sealed class AccountingAssignmentSnapshot : IAccountingAssignment
{
    public string? CostCenterCode { get; set; }
    public string? AccountingItemCode { get; set; }
    public string? DistributionRuleCode { get; set; }
    public string? LedgerAccountCode { get; set; }

    /// <summary>Tira uma foto dos campos de <paramref name="source"/>.</summary>
    public static AccountingAssignmentSnapshot From(IAccountingAssignment source)
    {
        var snapshot = new AccountingAssignmentSnapshot();
        Copy(source, snapshot);
        return snapshot;
    }

    /// <summary>
    /// Foto dos valores originais de uma entidade rastreada; <paramref name="getOriginal"/>
    /// devolve o valor original pelo nome da propriedade (ex.: <c>n => entry.OriginalValues[n]</c>).
    /// </summary>
    public static AccountingAssignmentSnapshot FromOriginal(Func<string, object?> getOriginal) => new()
    {
        CostCenterCode = getOriginal(nameof(IAccountingAssignment.CostCenterCode)) as string,
        AccountingItemCode = getOriginal(nameof(IAccountingAssignment.AccountingItemCode)) as string,
        DistributionRuleCode = getOriginal(nameof(IAccountingAssignment.DistributionRuleCode)) as string,
        LedgerAccountCode = getOriginal(nameof(IAccountingAssignment.LedgerAccountCode)) as string,
    };

    /// <summary>Copia os quatro campos de <paramref name="from"/> para <paramref name="to"/>.</summary>
    public static void Copy(IAccountingAssignment from, IAccountingAssignment to)
    {
        to.CostCenterCode = from.CostCenterCode;
        to.AccountingItemCode = from.AccountingItemCode;
        to.DistributionRuleCode = from.DistributionRuleCode;
        to.LedgerAccountCode = from.LedgerAccountCode;
    }
}
