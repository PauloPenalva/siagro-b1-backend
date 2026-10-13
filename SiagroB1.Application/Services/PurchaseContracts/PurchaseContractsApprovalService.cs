using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using SiagroB1.Application.Services.Financials;
using SiagroB1.Application.Services.Notifications;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.PurchaseContracts;

public class PurchaseContractsApprovalService(
    AppDbContext context,
    ContractNotificationOutboxService notificationOutbox,
    FinancialDocumentsGenerateService financialDocuments,
    IConfiguration configuration)
{
    public async Task ExecuteAsync(Guid key, string? comments, string approvedBy)
    {
        var contract = await context.PurchaseContracts
            .Include(x => x.PriceFixations)
            .FirstOrDefaultAsync(x => x.Key == key && x.Status == ContractStatus.InApproval)  ??
                       throw new NotFoundException($"Contract with key {key} not found or not draft.");

        // Spec 2026-10-13 D5/D7: em STANDALONE o contrato só aprova com natureza de operação — é ela que dá as
        // alíquotas de Funrural do pagamento ao produtor. Todas as filiais STANDALONE, emitam NF-e ou não.
        // Antes de qualquer atribuição: nada pode ficar sujo no contexto se recusar.
        if (ErpMode.IsStandalone(configuration) &&
            !await context.PurchaseContractFiscalComplements.AnyAsync(x => x.PurchaseContractKey == key && x.UsageCode != null))
        {
            throw new DefaultException("Informe a natureza de operação do contrato (Complemento Fiscal) antes de aprovar.");
        }

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
            await financialDocuments.EnqueueForPurchaseFixationAsync(contract, fixation, approvedBy);
        }

        await context.SaveChangesAsync();
    }
}