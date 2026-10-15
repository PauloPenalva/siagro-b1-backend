using SiagroB1.Application.Services.Financials;
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
    ContractNotificationOutboxService notificationOutbox,
    FinancialDocumentsCancelService financialDocuments)
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

        // Em rascunho o contrato pode ser excluído, e a FK Restrict do washout (de QUALQUER status)
        // barraria a exclusão com erro 547.
        if (await db.Context.SalesContractsWashouts.AnyAsync(w => w.SalesContractKey == contract.Key))
            throw new BusinessException(resource["SALES_CONTRACT_HAS_WASHOUTS"]);

        contract.Status = ContractStatus.Draft;
        contract.UpdatedBy = userName;
        contract.UpdatedAt = DateTime.Now;
        notificationOutbox.Register(contract, NotificationEventType.ApprovalWithdrawn, userName);

        // O provisório nasceu da aprovação e morre com ela; reaprovar gera um novo. Sem isso o
        // rascunho ficava com título a receber aberto, e a exclusão dele estourava na FK (547).
        // O adiantamento não é tocado: retirar a aprovação não desfaz o que foi combinado em dinheiro.
        await financialDocuments.EnqueueCancelByContractAsync(
            purchaseContractKey: null,
            salesContractKey: contract.Key,
            reason: "Aprovação retirada",
            userName: userName);

        await db.SaveChangesAsync();
    }
}