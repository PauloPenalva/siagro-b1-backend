using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Financials;

public class FinancialAccountsDeleteService(IUnitOfWork db)
{
    public async Task ExecuteAsync(string code)
    {
        var existing = await db.Context.FinancialAccounts.FirstOrDefaultAsync(x => x.Code == code)
                       ?? throw new NotFoundException($"Conta financeira {code} não encontrada.");

        if (await db.Context.FinancialSettlements.AnyAsync(x => x.FinancialAccountCode == code))
            throw new ApplicationException(
                "A conta possui baixas e não pode ser excluída. Inative-a.");

        db.Context.FinancialAccounts.Remove(existing);
        await db.SaveChangesAsync();
    }
}
