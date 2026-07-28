using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Notifications;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.PurchaseContracts;

public class PurchaseContractsSendApprovalService(
    AppDbContext db,
    ContractNotificationOutboxService notificationOutbox)
{
    public async Task ExecuteAsync(Guid key, string userName)
    {
        var contract = await db.PurchaseContracts
            .FirstOrDefaultAsync(x => x.Key == key) ??
                       throw new NotFoundException("Purchase contract not found.");

        if (contract.Status != ContractStatus.Draft)
        {
            throw new ApplicationException("Purchase contract not in draft state.");
        }

        contract.Status = ContractStatus.InApproval;
        contract.UpdatedBy = userName;
        contract.UpdatedAt = DateTime.Now;

        // Antes do SaveChanges: a linha da outbox precisa gravar na MESMA transação da mudança
        // de status, para que uma falha aqui não deixe notificação de algo que não aconteceu.
        notificationOutbox.Register(contract, NotificationEventType.SentForApproval, userName);

        await db.SaveChangesAsync();
    }
}