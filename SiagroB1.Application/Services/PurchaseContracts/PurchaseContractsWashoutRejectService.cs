using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Notifications;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.PurchaseContracts;

/// <summary>Rejeita o washout em aprovação e devolve o volume reservado ao saldo.</summary>
public class PurchaseContractsWashoutRejectService(
    AppDbContext context,
    PurchaseContractsWashedOutVolumeService washedOutVolumeService,
    PurchaseContractsChangeLogService changeLog,
    ContractNotificationOutboxService notificationOutbox)
{
    public async Task ExecuteAsync(Guid washoutKey, string? comments, string rejectedBy)
    {
        if (string.IsNullOrWhiteSpace(comments))
            throw new ApplicationException("Informe o motivo da rejeição.");

        if (comments.Length > 500)
            throw new ApplicationException("O motivo deve ter no máximo 500 caracteres.");

        var washout = await context.PurchaseContractsWashouts
                          .Include(x => x.PurchaseContract)
                          .FirstOrDefaultAsync(x => x.Key == washoutKey)
                      ?? throw new NotFoundException("Washout não encontrado.");

        if (washout.Status != PurchaseContractWashoutStatus.InApproval)
            throw new ApplicationException(
                $"Só é possível rejeitar washout em aprovação. Status atual: {washout.Status}.");

        var contract = washout.PurchaseContract
                       ?? throw new NotFoundException("Contrato de compra não encontrado.");

        var previous = ContractChangeLogFields.DescribeWashout(washout, contract.UnitOfMeasureCode);

        await using var transaction = await context.Database.BeginTransactionAsync();

        washout.Status = PurchaseContractWashoutStatus.Rejected;
        washout.ApprovalComments = comments.Trim();
        washout.CanceledBy = rejectedBy;
        washout.CanceledAt = DateTime.Now;
        washout.UpdatedAt = DateTime.Now;
        washout.UpdatedBy = rejectedBy;

        changeLog.Register(
            contract.Key,
            ContractChangeLogFields.Washout,
            previous,
            ContractChangeLogFields.DescribeWashout(washout, contract.UnitOfMeasureCode),
            rejectedBy);

        notificationOutbox.Register(contract, NotificationEventType.WashoutRejected, rejectedBy);

        // Salva ANTES de recalcular: recalcular antes somaria o rejeitado de volta.
        await context.SaveChangesAsync();

        await washedOutVolumeService.RecalculateAsync(contract);
        await context.SaveChangesAsync();

        await transaction.CommitAsync();
    }
}
