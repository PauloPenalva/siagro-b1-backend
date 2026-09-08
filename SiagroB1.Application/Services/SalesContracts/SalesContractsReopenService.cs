using SiagroB1.Application.Services.Financials;
using SiagroB1.Application.Services.Notifications;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.SalesContracts;

public class SalesContractsReopenService(
    AppDbContext context,
    ContractNotificationOutboxService notificationOutbox,
    FinancialDocumentsGenerateService financialDocuments)
{
    public async Task ExecuteAsync(Guid key, string userName)
    {
        var contract = await context.SalesContracts
                           .Include(x => x.PriceFixations)
                           .FirstOrDefaultAsync(x => x.Key == key && x.Status == ContractStatus.Finished)
                       ?? throw new NotFoundException("Contrato não encontrado ou não está encerrado.");

        contract.Status = ContractStatus.Approved;
        contract.UpdatedAt = DateTime.Now;
        contract.UpdatedBy = userName;

        notificationOutbox.Register(contract, NotificationEventType.Reopened, userName);

        // Regenera pelo MESMO gerador idempotente, recomputando o valor do estado atual das
        // fixações. Simétrico com o encerramento, e sem estado novo para manter.
        foreach (var fixation in contract.PriceFixations
                     .Where(f => f.Status == PriceFixationStatus.Confirmed))
        {
            await financialDocuments.EnqueueForSalesFixationAsync(contract, fixation, userName);
        }

        await context.SaveChangesAsync();
    }
}
