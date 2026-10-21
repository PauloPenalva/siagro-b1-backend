using SiagroB1.Domain.Entities;

namespace SiagroB1.Application.Services.FiscalStock;

public interface IFiscalStockPeriodsService
{
    IQueryable<FiscalStockPeriod> QueryAll();

    Task<FiscalStockPeriod> CloseAsync(string branchCode, int year, int month, string userName);

    Task<FiscalStockPeriod> ReopenAsync(string branchCode, int year, int month, string? reason, string userName);
}
