using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Financials;
using SiagroB1.Application.Services.Notifications;
using SiagroB1.Application.Services.PurchaseContracts;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.SalesContracts;

/// <summary>
/// Estorna um washout de venda aprovado: cancela o título a receber, restaura o provisório da fixação (o gerador
/// cria um novo se ele tinha sido cancelado por volume zero) e devolve o volume ao saldo. Recusado se o título já
/// teve baixa ou se o contrato não está aprovado.
/// </summary>
public class SalesContractsWashoutReverseService(
    AppDbContext context,
    SalesContractsWashedOutVolumeService washedOutVolumeService,
    SalesContractsChangeLogService changeLog,
    ContractNotificationOutboxService notificationOutbox,
    FinancialDocumentsAdjustProvisionalService provisionalAdjust,
    FinancialDocumentsCancelService financialDocumentsCancel)
{
    public async Task ExecuteAsync(Guid washoutKey, string? reason, string reversedBy)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new ApplicationException("Informe o motivo do estorno.");

        if (reason.Length > 500)
            throw new ApplicationException("O motivo deve ter no máximo 500 caracteres.");

        var washout = await context.SalesContractsWashouts
                          .Include(x => x.SalesContract)
                          .Include(x => x.PriceFixation)
                          .FirstOrDefaultAsync(x => x.Key == washoutKey)
                      ?? throw new NotFoundException("Washout não encontrado.");

        if (washout.Status != SalesContractWashoutStatus.Approved)
            throw new ApplicationException(
                $"Só é possível estornar washout aprovado. Status atual: {washout.Status}.");

        var contract = washout.SalesContract
                       ?? throw new NotFoundException("Contrato de venda não encontrado.");

        if (contract.Status == ContractStatus.Finished)
            throw new ApplicationException(
                "Contrato precisa estar aprovado para estornar washout. Reabra o contrato antes.");

        if (contract.Status != ContractStatus.Approved)
            throw new ApplicationException("Contrato precisa estar aprovado para estornar washout.");

        // Primeiro o título: é a trava de baixa, e precisa falhar ANTES de qualquer mutação.
        if (washout.FinancialDocumentKey is { } receivableKey)
            await financialDocumentsCancel.EnqueueCancelByKeyAsync(
                receivableKey, PurchaseContractsWashoutReverseService.ReceivableCancellationReason, reversedBy);

        var previous = ContractChangeLogFields.DescribeWashout(washout, contract.UnitOfMeasureCode);

        washout.Status = SalesContractWashoutStatus.Reversed;
        washout.ReversalReason = reason.Trim();
        washout.CanceledBy = reversedBy;
        washout.CanceledAt = DateTime.Now;
        washout.UpdatedAt = DateTime.Now;
        washout.UpdatedBy = reversedBy;

        // ANTES da transação: se o provisório tinha sido cancelado por volume zero, o gerador busca número por Dapper.
        if (washout.FixedVolume > 0 && washout.PriceFixation is { } fixation)
        {
            var stillWashed = await SalesContractsWashedOutVolumeService
                .ApprovedFixedVolumeAsync(context, fixation.Key, washout.Key);

            await provisionalAdjust.EnqueueForSalesFixationAsync(
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
