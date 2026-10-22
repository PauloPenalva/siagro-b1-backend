using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Accounting;

public interface IAccountingSettingsService
{
    Task<AccountingSettings> GetAsync();

    Task<AccountingSettings> SaveAsync(AccountingSettings settings, string user);
}

/// <summary>Configurações contábeis (linha única). Sem gravação anterior devolve os padrões, sem data de corte.</summary>
public class AccountingSettingsService(IUnitOfWork db) : IAccountingSettingsService
{
    public async Task<AccountingSettings> GetAsync() =>
        await db.Context.AccountingSettings.AsNoTracking().FirstOrDefaultAsync(s => s.Key == 1) ?? new AccountingSettings();

    public async Task<AccountingSettings> SaveAsync(AccountingSettings settings, string user)
    {
        var closing = string.IsNullOrWhiteSpace(settings.DefaultResultClosingAccountCode)
            ? null
            : settings.DefaultResultClosingAccountCode.Trim();
        if (closing is not null && !await db.Context.LedgerAccounts.AnyAsync(a => a.Code == closing && a.AllowsPosting &&
                (a.Type == LedgerAccountType.Equity || a.Type == LedgerAccountType.Liability)))
            throw new DefaultException(
                $"A conta de apuração {closing} precisa ser analítica, de patrimônio líquido ou passivo.");

        var row = await db.Context.AccountingSettings.FirstOrDefaultAsync(s => s.Key == 1);
        if (row is null)
        {
            row = new AccountingSettings();
            db.Context.AccountingSettings.Add(row);
        }

        row.CutoverDate = settings.CutoverDate?.Date;
        row.DefaultResultClosingAccountCode = closing;
        row.ClosingFrequency = Enum.IsDefined(settings.ClosingFrequency) ? settings.ClosingFrequency : AccountingClosingFrequency.Monthly;
        row.UpdatedAt = DateTime.Now;
        row.UpdatedBy = user;
        await db.SaveChangesAsync();
        return row;
    }
}
