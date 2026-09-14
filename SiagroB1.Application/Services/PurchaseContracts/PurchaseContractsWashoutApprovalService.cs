using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Financials;
using SiagroB1.Application.Services.Notifications;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.PurchaseContracts;

/// <summary>
/// Aprova o washout: reduz o provisório a pagar da fixação (no lugar) e gera o título a receber
/// do produtor. O volume já estava reservado desde o registro.
/// </summary>
public class PurchaseContractsWashoutApprovalService(
    AppDbContext context,
    PurchaseContractsWashedOutVolumeService washedOutVolumeService,
    PurchaseContractsChangeLogService changeLog,
    ContractNotificationOutboxService notificationOutbox,
    FinancialDocumentsAdjustProvisionalService provisionalAdjust,
    FinancialDocumentsGenerateWashoutReceivableService receivableGenerator)
{
    public async Task ExecuteAsync(Guid washoutKey, string? comments, string approvedBy)
    {
        if (comments != null && comments.Length > 500)
            throw new ApplicationException("O comentário deve ter no máximo 500 caracteres.");

        var washout = await context.PurchaseContractsWashouts
                          .Include(x => x.PurchaseContract).ThenInclude(c => c!.ShipmentReleases)
                          .Include(x => x.PriceFixation)
                          .FirstOrDefaultAsync(x => x.Key == washoutKey)
                      ?? throw new NotFoundException("Washout não encontrado.");

        if (washout.Status != PurchaseContractWashoutStatus.InApproval)
            throw new ApplicationException(
                $"Só é possível aprovar washout em aprovação. Status atual: {washout.Status}.");

        var contract = washout.PurchaseContract
                       ?? throw new NotFoundException("Contrato de compra não encontrado.");

        await PurchaseContractsWashoutGuard.ValidateAsync(context, contract, washout, washout.PriceFixation);

        var previous = ContractChangeLogFields.DescribeWashout(washout, contract.UnitOfMeasureCode);

        washout.Status = PurchaseContractWashoutStatus.Approved;
        washout.ApprovedBy = approvedBy;
        washout.ApprovedAt = DateTime.Now;
        washout.ApprovalComments = comments?.Trim();
        washout.UpdatedAt = DateTime.Now;
        washout.UpdatedBy = approvedBy;

        // ANTES de abrir a transação: o ajuste (quando não há provisório aberto) e o título a
        // receber buscam número em DOC_NUMBERS por Dapper, numa conexão fora da transação do EF.
        if (washout.FixedVolume > 0 && washout.PriceFixation is { } fixation)
        {
            // O banco ainda não vê este washout como aprovado: soma os OUTROS e subtrai este.
            var otherApproved = await PurchaseContractsWashedOutVolumeService
                .ApprovedFixedVolumeAsync(context, fixation.Key, washout.Key);

            await provisionalAdjust.EnqueueForPurchaseFixationAsync(
                contract, fixation, fixation.FixationVolume - otherApproved - washout.FixedVolume, approvedBy);
        }

        if (washout.Amount > 0)
        {
            var receivable = await receivableGenerator.EnqueueAsync(contract, washout, approvedBy);
            washout.FinancialDocumentKey = receivable.Key;
        }

        await using var transaction = await context.Database.BeginTransactionAsync();

        changeLog.Register(
            contract.Key,
            ContractChangeLogFields.Washout,
            previous,
            ContractChangeLogFields.DescribeWashout(washout, contract.UnitOfMeasureCode),
            approvedBy);

        notificationOutbox.Register(contract, NotificationEventType.WashoutApproved, approvedBy);

        // Salva o status ANTES de recalcular (a soma lê o banco). O total não muda — InApproval e
        // Approved reservam igual —, mas manter a ordem evita depender dessa coincidência.
        await context.SaveChangesAsync();

        await washedOutVolumeService.RecalculateAsync(contract);
        await context.SaveChangesAsync();

        await transaction.CommitAsync();
    }
}
