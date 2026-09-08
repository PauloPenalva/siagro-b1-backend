using SiagroB1.Application.Services.Financials;
using SiagroB1.Application.Services.Notifications;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.SalesContracts;

public class SalesContractsApprovalService(
    AppDbContext context,
    ContractNotificationOutboxService notificationOutbox,
    FinancialDocumentsGenerateService financialDocuments)
{
    public async Task ExecuteAsync(Guid key, string? comments, string approvedBy)
    {
        var contract = await context.SalesContracts
            .Include(x => x.PriceFixations)
            .FirstOrDefaultAsync(x => x.Key == key && x.Status == ContractStatus.InApproval)  ??
                       throw new NotFoundException($"Contract with key {key} not found or not draft.");

        contract.Status = ContractStatus.Approved;
        contract.ApprovalComments = comments;
        contract.ApprovedAt = DateTime.Now;
        contract.ApprovedBy = approvedBy;

        notificationOutbox.Register(contract, NotificationEventType.Approved, approvedBy);

        // Um documento financeiro provisório por fixação CONFIRMADA. Contrato de preço fixo já
        // nasce com uma; contrato a fixar não tem nenhuma aqui e recebe a sua quando cada
        // fixação for confirmada. Sem if de tipo, de propósito.
        // Enfileirado ANTES do SaveChanges: o documento e o status do contrato são atômicos.
        foreach (var fixation in contract.PriceFixations
                     .Where(f => f.Status == PriceFixationStatus.Confirmed))
        {
            await financialDocuments.EnqueueForSalesFixationAsync(contract, fixation, approvedBy);
        }

        await context.SaveChangesAsync();
    }
}