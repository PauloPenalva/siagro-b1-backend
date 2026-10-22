using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;

namespace SiagroB1.Application.Services.Accounting;

/// <summary>Regras do plano de contas usadas pelo cadastro e pela gravação de lançamentos.</summary>
public static class LedgerAccountRules
{
    /// <summary>Conta aceita lançamento na data: analítica, ativa e fora do intervalo de bloqueio.</summary>
    public static bool IsPostable(LedgerAccount account, DateTime date)
    {
        if (!account.AllowsPosting || account.Inactive) return false;
        if (account.BlockedFrom is { } from && date.Date >= from.Date &&
            (account.BlockedTo == null || date.Date <= account.BlockedTo.Value.Date))
            return false;
        return true;
    }

    /// <summary>Natureza padrão pelo tipo: ativo e despesa devedoras; o resto credora.</summary>
    public static LedgerAccountNature DefaultNature(LedgerAccountType type) =>
        type is LedgerAccountType.Asset or LedgerAccountType.Expense
            ? LedgerAccountNature.Debit
            : LedgerAccountNature.Credit;

    public static bool IsResultAccount(LedgerAccountType? type) =>
        type is LedgerAccountType.Revenue or LedgerAccountType.Expense;
}
