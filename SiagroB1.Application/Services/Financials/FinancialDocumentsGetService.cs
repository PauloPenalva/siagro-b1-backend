using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.Financials;

public class FinancialDocumentsGetService(IUnitOfWork db)
{
    public IQueryable<FinancialDocument> QueryAll() =>
        db.Context.FinancialDocuments.AsNoTracking();

    public Task<FinancialDocument?> GetByIdAsync(Guid key) =>
        db.Context.FinancialDocuments
            .Include(x => x.Settlements)
            .Include(x => x.ChangeLogs)
            .FirstOrDefaultAsync(x => x.Key == key);
}
