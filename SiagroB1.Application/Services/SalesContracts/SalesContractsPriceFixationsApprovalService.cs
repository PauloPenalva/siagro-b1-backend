using SiagroB1.Application.Services.Financials;
using SiagroB1.Application.Services.Notifications;
using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.SalesContracts;

public class SalesContractsPriceFixationsApprovalService(
    AppDbContext context,
    SalesContractsFixedVolumeService fixedVolumeService,
    SalesContractsChangeLogService changeLog,
    ContractNotificationOutboxService notificationOutbox,
    FinancialDocumentsGenerateService financialDocuments)
{
    public async Task ExecuteAsync(Guid fixationKey, string? comments, string approvedBy)
    {
        var fixation = await context.SalesContractsPriceFixations
                           .Include(x => x.SalesContract)
                           .FirstOrDefaultAsync(x => x.Key == fixationKey)
                       ?? throw new NotFoundException("Fixação de preço não encontrada.");

        if (fixation.Status != PriceFixationStatus.InApproval)
            throw new ApplicationException(
                $"Só é possível aprovar fixação em aprovação. Status atual: {fixation.Status}.");

        var contract = fixation.SalesContract
                       ?? throw new NotFoundException("Contrato de venda não encontrado.");

        if (contract.Status != ContractStatus.Approved)
            throw new ApplicationException(
                "Contrato precisa estar aprovado para movimentar fixações. " +
                "Reabra o contrato antes de aprovar a fixação.");

        fixation.Status = PriceFixationStatus.Confirmed;

        // ANTES de abrir a transação: o gerador busca o número em DOC_NUMBERS por Dapper, numa
        // conexão que NÃO participa da transação do EF; feito lá dentro, o UPDLOCK ficaria
        // preso e serializaria a criação de documento no sistema inteiro.
        await financialDocuments.EnqueueForSalesFixationAsync(contract, fixation, approvedBy);

        await using var transaction = await context.Database.BeginTransactionAsync();

        var previous = ContractChangeLogFields.DescribePriceFixation(
            fixation.FixationVolume, fixation.FixationPrice, PriceFixationStatus.InApproval,
            contract.UnitOfMeasureCode);

        fixation.ApprovedBy = approvedBy;
        fixation.ApprovedAt = DateTime.Now;
        fixation.ApprovalComments = comments;
        fixation.UpdatedAt = DateTime.Now;
        fixation.UpdatedBy = approvedBy;

        changeLog.Register(
            contract.Key,
            ContractChangeLogFields.PriceFixation,
            previous,
            ContractChangeLogFields.DescribePriceFixation(
                fixation.FixationVolume, fixation.FixationPrice, fixation.Status,
                contract.UnitOfMeasureCode),
            approvedBy);
        notificationOutbox.RegisterPriceFixation(
            contract, fixation, NotificationEventType.PriceFixationApproved, approvedBy);


        // Salva o status ANTES de recalcular: RecalculateAsync consulta o banco e não
        // enxerga mudanças apenas rastreadas em memória. Aqui o total não muda (InApproval
        // e Confirmed reservam volume igual), mas manter a mesma ordem dos demais serviços
        // evita que a corretude dependa dessa coincidência.
        await context.SaveChangesAsync();

        await fixedVolumeService.RecalculateAsync(contract);
        await context.SaveChangesAsync();

        await transaction.CommitAsync();
    }
}
