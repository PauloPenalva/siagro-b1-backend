using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Financials;

public class FinancialAccountsCreateService(IUnitOfWork db)
{
    public async Task<FinancialAccount> ExecuteAsync(FinancialAccount account, string userName)
    {
        await ValidateAsync(account);

        account.Code = account.Code.Trim().ToUpperInvariant();

        try
        {
            await db.BeginTransactionAsync();
            db.Context.FinancialAccounts.Add(account);
            await db.SaveChangesAsync();
            await db.CommitAsync();
        }
        catch
        {
            await db.RollbackAsync();
            throw;
        }

        return account;
    }

    private async Task ValidateAsync(FinancialAccount account)
    {
        if (string.IsNullOrWhiteSpace(account.Code))
            throw new ApplicationException("Informe o código da conta financeira.");

        if (string.IsNullOrWhiteSpace(account.Name))
            throw new ApplicationException("Informe o nome da conta financeira.");

        if (account.Type == FinancialAccountType.Bank)
        {
            if (string.IsNullOrWhiteSpace(account.BankCode))
                throw new ApplicationException("Informe o código do banco.");

            if (string.IsNullOrWhiteSpace(account.BankAccountNumber))
                throw new ApplicationException("Informe o número da conta bancária.");
        }

        var code = account.Code.Trim().ToUpperInvariant();

        if (await db.Context.FinancialAccounts.AnyAsync(x => x.Code == code))
            throw new ApplicationException($"A conta financeira {code} já existe.");
    }
}
