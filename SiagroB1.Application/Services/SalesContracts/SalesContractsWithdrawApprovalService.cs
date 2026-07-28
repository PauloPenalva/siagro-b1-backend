using SiagroB1.Application.Services.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.SalesContracts;

public class SalesContractsWithdrawApprovalService(
    IUnitOfWork db,
    IStringLocalizer<Resource> resource,
    ContractNotificationOutboxService notificationOutbox)
{
    public async Task ExecuteAsync(Guid key, string userName)
    {
        var contract = await db.Context.SalesContracts
                           .Include(s => s.SalesInvoiceItems)
                           .ThenInclude(si  => si.SalesInvoice)
                           .FirstOrDefaultAsync(x => x.Key == key) ??
                                throw new NotFoundException(resource["SALES_CONTRACT_NOT_FOUND"]);

        if (contract.Status is ContractStatus.Finished or ContractStatus.Canceled or ContractStatus.Rejected)
            throw new ApplicationException(resource["SALES_CONTRACT_NOT_IN_APPROVAL_STATUS"]);
        
        if (contract.HasInvoices)
            throw new BusinessException(resource["SALES_CONTRACT_HAS_INVOICES"]);

        contract.Status = ContractStatus.Draft;
        contract.UpdatedBy = userName;
        contract.UpdatedAt = DateTime.Now;
        notificationOutbox.Register(contract, NotificationEventType.ApprovalWithdrawn, userName);

        await db.SaveChangesAsync();
    }
}