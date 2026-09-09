using SiagroB1.Application.Services.Financials;
using SiagroB1.Application.Services.Notifications;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.SalesContracts;

public class SalesContractsCancelService(
    IUnitOfWork db,
    ContractNotificationOutboxService notificationOutbox,
    FinancialDocumentsCancelService financialDocuments,
    FinancialDocumentsContractCancellationGuardService cancellationGuard)
{
    public async Task ExecuteAsync(Guid key, string? comments, string userName)
    {
        var contract = await db.Context.SalesContracts
                            .Include(x => x.SalesInvoiceItems)
                            .ThenInclude(x => x.SalesInvoice)
                            .FirstOrDefaultAsync(x => x.Key == key
                                                      && x.Status == ContractStatus.Approved) ??
                       throw new NotFoundException($"Contrato com a chave {key} não encontrado ou não está aprovado.");

        if (contract.SalesInvoiceItems.Any(x =>
               x.SalesInvoice.InvoiceStatus != InvoiceStatus.Cancelled))
        {
            throw new ApplicationException("Contrato possui movimentos. Não é possivel cancelar, considere fazer washout.");
        }

        // Dinheiro tem o mesmo peso que grão: adiantamento pago barra o cancelamento. ANTES de
        // qualquer atribuição, para a operação inteira falhar sem efeito colateral.
        await cancellationGuard.EnsureCanCancelAsync(
            purchaseContractKey: null, salesContractKey: contract.Key);

        contract.Status = ContractStatus.Canceled;
        contract.ApprovalComments = comments;
        contract.CanceledAt = DateTime.Now;
        contract.CanceledBy = userName;

        notificationOutbox.Register(contract, NotificationEventType.Canceled, userName);

        await financialDocuments.EnqueueCancelByContractAsync(
            purchaseContractKey: null,
            salesContractKey: contract.Key,
            reason: "Contrato cancelado",
            userName: userName,
            includeUnpaidAdvances: true);

        await db.SaveChangesAsync();
    }
}