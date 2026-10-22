using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Infra.Accounting;

/// <summary>
/// Trava do calendário contábil (CtbValiDt do Protheus): nenhum lançamento, manual ou automático, cai num período
/// Fechado ou Bloqueado. Data sem período cadastrado conta como aberta, para não travar quem nunca usou o módulo.
/// </summary>
public class AccountingPeriodGuard(IUnitOfWork db)
{
    public async Task EnsureOpenAsync(DateTime date, CancellationToken ct = default)
    {
        var status = await db.Context.AccountingPeriods
            .Where(p => p.Year == date.Year && p.Month == date.Month)
            .Select(p => (AccountingPeriodStatus?)p.Status)
            .FirstOrDefaultAsync(ct);

        if (status is null or AccountingPeriodStatus.Open) return;

        var word = status == AccountingPeriodStatus.Blocked ? "bloqueado" : "fechado";
        throw new DefaultException($"Período contábil {date:MM/yyyy} {word}.");
    }

    /// <summary>A própria data, se o mês dela estiver aberto; senão o 1º dia do próximo mês aberto.</summary>
    public async Task<DateTime> FirstOpenDateOnOrAfterAsync(DateTime date, CancellationToken ct = default)
    {
        var current = date.Date;
        // Limite de segurança: um calendário inteiro fechado por mais de 10 anos não é cenário real.
        for (var i = 0; i < 120; i++)
        {
            var closed = await db.Context.AccountingPeriods.AnyAsync(p =>
                p.Year == current.Year && p.Month == current.Month && p.Status != AccountingPeriodStatus.Open, ct);
            if (!closed) return current;
            current = new DateTime(current.Year, current.Month, 1).AddMonths(1);
        }

        throw new DefaultException("Nenhum período contábil aberto encontrado.");
    }
}
