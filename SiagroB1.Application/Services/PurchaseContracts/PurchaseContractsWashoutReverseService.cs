using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Financials;
using SiagroB1.Application.Services.Notifications;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.PurchaseContracts;

/// <summary>
/// Estorna um washout aprovado: cancela o título a receber, restaura o provisório da fixação e
/// devolve o volume ao saldo. Recusado se o título já teve baixa — o dinheiro entrou, e o acerto
/// passa pelo estorno da baixa antes.
/// </summary>
public class PurchaseContractsWashoutReverseService(
    AppDbContext context,
    PurchaseContractsWashedOutVolumeService washedOutVolumeService,
    PurchaseContractsChangeLogService changeLog,
    ContractNotificationOutboxService notificationOutbox,
    FinancialDocumentsAdjustProvisionalService provisionalAdjust,
    FinancialDocumentsCancelService financialDocumentsCancel)
{
    public const string ReceivableCancellationReason = "Washout estornado";

    public async Task ExecuteAsync(Guid washoutKey, string? reason, string reversedBy)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ApplicationException("Informe o motivo do estorno.");

        if (reason.Length > 500)
            throw new ApplicationException("O motivo deve ter no máximo 500 caracteres.");

        var washout = await context.PurchaseContractsWashouts
                          .Include(x => x.PurchaseContract)
                          .Include(x => x.PriceFixation)
                          .FirstOrDefaultAsync(x => x.Key == washoutKey)
                      ?? throw new NotFoundException("Washout não encontrado.");

        if (washout.Status != PurchaseContractWashoutStatus.Approved)
            throw new ApplicationException(
                $"Só é possível estornar washout aprovado. Status atual: {washout.Status}.");

        var contract = washout.PurchaseContract
                       ?? throw new NotFoundException("Contrato de compra não encontrado.");

        // F6 (revisão final): "Reabra o contrato antes" só faz sentido para Finished — é a ação
        // que devolve o contrato a Approved. Canceled (ou qualquer outro status) não reabre.
        if (contract.Status == ContractStatus.Finished)
            throw new ApplicationException(
                "Contrato precisa estar aprovado para estornar washout. Reabra o contrato antes.");

        if (contract.Status != ContractStatus.Approved)
            throw new ApplicationException("Contrato precisa estar aprovado para estornar washout.");

        // Primeiro o título: é a trava de baixa, e precisa falhar ANTES de qualquer mutação.
        if (washout.FinancialDocumentKey is { } receivableKey)
            await financialDocumentsCancel.EnqueueCancelByKeyAsync(receivableKey, ReceivableCancellationReason, reversedBy);

        var previous = ContractChangeLogFields.DescribeWashout(washout, contract.UnitOfMeasureCode);

        washout.Status = PurchaseContractWashoutStatus.Reversed;
        washout.ReversalReason = reason.Trim();
        washout.CanceledBy = reversedBy;
        washout.CanceledAt = DateTime.Now;
        washout.UpdatedAt = DateTime.Now;
        washout.UpdatedBy = reversedBy;

        // ANTES da transação: se o provisório tinha sido cancelado por volume zero, o gerador
        // busca número novo por Dapper.
        if (washout.FixedVolume > 0 && washout.PriceFixation is { } fixation)
        {
            var stillWashed = await PurchaseContractsWashedOutVolumeService
                .ApprovedFixedVolumeAsync(context, fixation.Key, washout.Key);

            await provisionalAdjust.EnqueueForPurchaseFixationAsync(
                contract, fixation, fixation.FixationVolume - stillWashed, reversedBy);
        }

        await using var transaction = await context.Database.BeginTransactionAsync();

        changeLog.Register(
            contract.Key,
            ContractChangeLogFields.Washout,
            previous,
            ContractChangeLogFields.DescribeWashout(washout, contract.UnitOfMeasureCode),
            reversedBy);

        notificationOutbox.Register(contract, NotificationEventType.WashoutReversed, reversedBy);

        // Salva ANTES de recalcular: recalcular antes somaria o estornado de volta.
        await context.SaveChangesAsync();

        await washedOutVolumeService.RecalculateAsync(contract);
        await context.SaveChangesAsync();

        await transaction.CommitAsync();
    }
}
