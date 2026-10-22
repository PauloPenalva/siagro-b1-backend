using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Interfaces;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Accounting;

public interface IAccountingPeriodsService
{
    IQueryable<AccountingPeriod> QueryAll();

    Task<List<AccountingPeriod>> GenerateYearAsync(int year, string user);

    Task<AccountingPeriod> SetStatusAsync(int id, AccountingPeriodStatus status, string user, string? reason);
}

/// <summary>
/// Calendário contábil (CTG/CQD do Protheus). Fecha em ordem cronológica; reabrir um período fechado exige motivo,
/// o papel ADMIN e que o mês seguinte não esteja fechado. Bloquear/desbloquear é trava temporária, sem exigências.
/// </summary>
public class AccountingPeriodsService(IUnitOfWork db, IUserPermissions permissions) : IAccountingPeriodsService
{
    public IQueryable<AccountingPeriod> QueryAll() => db.Context.AccountingPeriods.AsNoTracking();

    public async Task<List<AccountingPeriod>> GenerateYearAsync(int year, string user)
    {
        if (year is < 2000 or > 2100) throw new DefaultException("Exercício inválido.");

        var existing = await db.Context.AccountingPeriods.Where(p => p.Year == year).Select(p => p.Month).ToListAsync();
        for (var month = 1; month <= 12; month++)
        {
            if (existing.Contains(month)) continue;
            var start = new DateTime(year, month, 1);
            db.Context.AccountingPeriods.Add(new AccountingPeriod
            {
                Year = year, Month = month, StartDate = start, EndDate = start.AddMonths(1).AddDays(-1),
            });
        }

        await db.SaveChangesAsync();
        return await db.Context.AccountingPeriods.Where(p => p.Year == year).OrderBy(p => p.Month).ToListAsync();
    }

    public async Task<AccountingPeriod> SetStatusAsync(int id, AccountingPeriodStatus status, string user, string? reason)
    {
        var period = await db.Context.AccountingPeriods.FirstOrDefaultAsync(p => p.Id == id)
                     ?? throw new DefaultException("Período contábil não encontrado.");
        if (period.Status == status) return period;

        var now = DateTime.Now;
        switch (status)
        {
            // Só se bloqueia um mês aberto: Fechado → Bloqueado → Aberto driblaria motivo e papel ADMIN da reabertura.
            case AccountingPeriodStatus.Blocked when period.Status != AccountingPeriodStatus.Open:
                throw new DefaultException("Só é possível bloquear um período aberto.");

            case AccountingPeriodStatus.Closed:
                await EnsurePreviousClosedAsync(period);
                period.ClosedAt = now;
                period.ClosedBy = user;
                break;

            case AccountingPeriodStatus.Open when period.Status == AccountingPeriodStatus.Closed:
                if (string.IsNullOrWhiteSpace(reason)) throw new DefaultException("Informe o motivo da reabertura.");
                if (!await permissions.HasRoleAsync(user, Domain.Constants.Roles.Admin))
                    throw new DefaultException("Somente o papel ADMIN pode reabrir período contábil.");
                await EnsureNextNotClosedAsync(period);
                period.ReopenedAt = now;
                period.ReopenedBy = user;
                period.ReopenReason = reason.Trim();
                break;
        }

        period.Status = status;
        await db.SaveChangesAsync();
        return period;
    }

    private async Task EnsurePreviousClosedAsync(AccountingPeriod period)
    {
        var previous = period.StartDate.AddMonths(-1);
        var status = await db.Context.AccountingPeriods
            .Where(p => p.Year == previous.Year && p.Month == previous.Month)
            .Select(p => (AccountingPeriodStatus?)p.Status).FirstOrDefaultAsync();
        if (status is not null and not AccountingPeriodStatus.Closed)
            throw new DefaultException($"Feche antes o período {previous:MM/yyyy}.");
    }

    private async Task EnsureNextNotClosedAsync(AccountingPeriod period)
    {
        var next = period.StartDate.AddMonths(1);
        if (await db.Context.AccountingPeriods.AnyAsync(p =>
                p.Year == next.Year && p.Month == next.Month && p.Status == AccountingPeriodStatus.Closed))
            throw new DefaultException($"Reabra antes o período {next:MM/yyyy}.");
    }
}
