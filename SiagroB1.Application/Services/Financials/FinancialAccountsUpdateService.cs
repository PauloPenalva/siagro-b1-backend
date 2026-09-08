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
}
