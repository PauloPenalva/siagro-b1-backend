using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Notifications;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.PurchaseContracts;

public class PurchaseContractsApprovalService(
    AppDbContext context,
    ContractNotificationOutboxService notificationOutbox)
{
    public async Task ExecuteAsync(Guid key, string? comments, string approvedBy)
    {
        var contract = await context.PurchaseContracts
            .FirstOrDefaultAsync(x => x.Key == key && x.Status == ContractStatus.InApproval)  ?? 
                       throw new NotFoundException($"Contract with key {key} not found or not draft.");
        
        contract.Status = ContractStatus.Approved;
        contract.ApprovalComments = comments;
        contract.ApprovedAt = DateTime.Now;
        contract.ApprovedBy = approvedBy;

        notificationOutbox.Register(contract, NotificationEventType.Approved, approvedBy);

        await context.SaveChangesAsync();
    }
}