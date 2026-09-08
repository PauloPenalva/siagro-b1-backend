using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Financials;

public class FinancialAccountsGetService(IUnitOfWork db)
{
    public IQueryable<FinancialAccount> QueryAll() =>
        db.Context.FinancialAccounts.AsNoTracking();

    public Task<FinancialAccount?> GetByIdAsync(string code) =>
        db.Context.FinancialAccounts.FirstOrDefaultAsync(x => x.Code == code);
}
