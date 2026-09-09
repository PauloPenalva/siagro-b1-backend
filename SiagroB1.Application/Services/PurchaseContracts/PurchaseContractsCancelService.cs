using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Financials;
using SiagroB1.Application.Services.Notifications;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.PurchaseContracts;

public class PurchaseContractsCancelService(
    IUnitOfWork db,
    ContractNotificationOutboxService notificationOutbox,
    FinancialDocumentsCancelService financialDocuments,
    FinancialDocumentsContractCancellationGuardService cancellationGuard)
{
    public async Task ExecuteAsync(Guid key, string? comments, string userName)
    {
        var contract = await db.Context.PurchaseContracts
                            .Include(x => x.Allocations)
                            .ThenInclude(a => a.StorageTransaction)
                            .FirstOrDefaultAsync(x => x.Key == key
                                                      && x.Status == ContractStatus.Approved) ??
                       throw new NotFoundException($"Contrato com a chave {key} não encontrado ou não está aprovado.");

        if (contract.Allocations.Any(x =>
                x.StorageTransaction?.TransactionStatus != StorageTransactionsStatus.Cancelled))
        {
            throw new ApplicationException("Contrato possui movimentos. Não é possivel cancelar, considere fazer washout.");
        }

        // Dinheiro tem o mesmo peso que grão: adiantamento pago barra o cancelamento. ANTES de
        // qualquer atribuição, para a operação inteira falhar sem efeito colateral.
        await cancellationGuard.EnsureCanCancelAsync(
            purchaseContractKey: contract.Key, salesContractKey: null);

        contract.Status = ContractStatus.Canceled;
        contract.ApprovalComments = comments;
        contract.CanceledAt = DateTime.Now;
        contract.CanceledBy = userName;

        notificationOutbox.Register(contract, NotificationEventType.Canceled, userName);

        await financialDocuments.EnqueueCancelByContractAsync(
            purchaseContractKey: contract.Key,
            salesContractKey: null,
            reason: "Contrato cancelado",
            userName: userName,
            includeUnpaidAdvances: true);

        await db.SaveChangesAsync();
    }
}