using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.SalesContracts;

public class SalesContractsWithdrawApprovalService(
    IUnitOfWork db,
    IStringLocalizer<Resource> resource)
{
    public async Task ExecuteAsync(Guid key, string userName)
    {
        var contract = await db.Context.SalesContracts
                           .Include(s => s.SalesInvoiceItems)
                           .FirstOrDefaultAsync(x => x.Key == key) ??
                                throw new NotFoundException(resource["SALES_CONTRACT_NOT_FOUND"]);

        if (contract.Status != ContractStatus.InApproval)
            throw new ApplicationException(resource["SALES_CONTRACT_NOT_IN_APPROVAL_STATUS"]);
        
        if (contract.HasInvoices)
            throw new BusinessException(resource["SALES_CONTRACT_HAS_INVOICES"]);

        contract.Status = ContractStatus.Draft;
        contract.UpdatedBy = userName;
        contract.UpdatedAt = DateTime.Now;
        await db.SaveChangesAsync();
    }
}