using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Notifications;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.PurchaseContracts;

/// <summary>
/// Estorno de fixação confirmada: desfaz a APROVAÇÃO e devolve a fixação para a
/// fila da diretoria (<see cref="PriceFixationStatus.InApproval"/>).
/// </summary>
/// <remarks>
/// Não vira <see cref="PriceFixationStatus.Canceled"/>: o estorno existe para
/// corrigir uma aprovação, não para matar o registro. Voltando a InApproval a
/// fixação fica de novo editável, excluível e reaprovável.
///
/// O volume continua reservado — InApproval reserva igual a Confirmed. Só o preço
/// sai do total, porque TotalPrice conta apenas Confirmed. Para devolver o volume
/// ao saldo, exclui-se a fixação ou a diretoria a rejeita.
/// </remarks>
public class PurchaseContractsPriceFixationsCancelService(
    AppDbContext context,
    PurchaseContractsFixedVolumeService fixedVolumeService,
    PurchaseContractsChangeLogService changeLog,
    ContractNotificationOutboxService notificationOutbox)
{
    public async Task ExecuteAsync(Guid fixationKey, string canceledBy)
    {
        var fixation = await context.PurchaseContractsPriceFixations
                           .Include(x => x.PurchaseContract)
                           .FirstOrDefaultAsync(x => x.Key == fixationKey)
                       ?? throw new NotFoundException("Fixação de preço não encontrada.");

        if (fixation.Status != PriceFixationStatus.Confirmed)
            throw new ApplicationException(
                $"Só é possível estornar fixação confirmada. Status atual: {fixation.Status}. " +
                "Fixação em aprovação deve ser rejeitada.");

        var contract = fixation.PurchaseContract
                       ?? throw new NotFoundException("Contrato de compra não encontrado.");

        if (contract.Status != ContractStatus.Approved)
            throw new ApplicationException(
                "Contrato precisa estar aprovado para movimentar fixações. " +
                "Reabra o contrato antes de estornar a fixação.");

        await using var transaction = await context.Database.BeginTransactionAsync();

        var previous = ContractChangeLogFields.DescribePriceFixation(
            fixation.FixationVolume, fixation.FixationPrice, fixation.Status,
            contract.UnitOfMeasureCode);

        fixation.Status = PriceFixationStatus.InApproval;

        // A aprovação anterior deixa de valer: manter aprovador e comentário faria a
        // fila da diretoria exibir uma fixação "pendente" já assinada por alguém.
        fixation.ApprovedBy = null;
        fixation.ApprovedAt = null;
        fixation.ApprovalComments = null;

        // Registra quem desfez, preservando o rastro do estorno.
        fixation.CanceledBy = canceledBy;
        fixation.CanceledAt = DateTime.Now;
        fixation.UpdatedAt = DateTime.Now;
        fixation.UpdatedBy = canceledBy;

        changeLog.Register(
            contract.Key,
            ContractChangeLogFields.PriceFixation,
            previous,
            ContractChangeLogFields.DescribePriceFixation(
                fixation.FixationVolume, fixation.FixationPrice, fixation.Status,
                contract.UnitOfMeasureCode),
            canceledBy);

        // Reversed, e não Canceled: apesar do nome do serviço, a fixação volta para
        // InApproval — o evento tem de dizer o que de fato aconteceu com ela.
        notificationOutbox.RegisterPriceFixation(
            contract, fixation, NotificationEventType.PriceFixationReversed, canceledBy);

        // Salva o status ANTES de recalcular: RecalculateAsync consulta o banco e não
        // enxerga mudanças apenas rastreadas em memória.
        await context.SaveChangesAsync();

        // FixedVolume não muda (InApproval reserva igual a Confirmed), mas recalculamos
        // pelo caminho único para não depender dessa coincidência.
        await fixedVolumeService.RecalculateAsync(contract);
        await context.SaveChangesAsync();

        await transaction.CommitAsync();
    }
}
