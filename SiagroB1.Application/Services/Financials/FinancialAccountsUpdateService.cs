using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Financials;

public class FinancialAccountsUpdateService(IUnitOfWork db)
{
    public async Task<FinancialAccount> ExecuteAsync(string code, FinancialAccount entity)
    {
        var existing = await db.Context.FinancialAccounts.FirstOrDefaultAsync(x => x.Code == code)
                       ?? throw new NotFoundException($"Conta financeira {code} não encontrada.");

        if (existing.Currency != entity.Currency &&
            await db.Context.FinancialSettlements.AnyAsync(x => x.FinancialAccountCode == code))
            throw new ApplicationException(
                "A conta já possui baixas: não é possível alterar a moeda.");

        db.Context.Entry(existing).CurrentValues.SetValues(entity);
        existing.Code = code;

        await db.SaveChangesAsync();
        return existing;
    }

    /// <summary>
    /// Alteração parcial (PATCH da tela): aplica só os campos enviados sobre a conta gravada, com a mesma trava de
    /// moeda do PUT. A chave não muda.
    /// </summary>
    public async Task<FinancialAccount> ExecuteAsync(string code, Action<FinancialAccount> applyChanges)
    {
        var existing = await db.Context.FinancialAccounts.FirstOrDefaultAsync(x => x.Code == code)
                       ?? throw new NotFoundException($"Conta financeira {code} não encontrada.");

        var originalCurrency = existing.Currency;
        applyChanges(existing);
        existing.Code = code;

        if (existing.Currency != originalCurrency &&
            await db.Context.FinancialSettlements.AnyAsync(x => x.FinancialAccountCode == code))
            throw new ApplicationException(
                "A conta já possui baixas: não é possível alterar a moeda.");

        await db.SaveChangesAsync();
        return existing;
    }
}
