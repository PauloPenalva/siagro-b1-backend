using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Financials;
using SiagroB1.Application.Services.Notifications;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.SalesContracts;

/// <summary>
/// Aprova o washout de venda: reduz o provisório a receber da fixação (no lugar, recusando abaixo do já composto)
/// e gera o título a receber do cliente. O volume já estava reservado desde o registro.
/// </summary>
public class SalesContractsWashoutApprovalService(
    AppDbContext context,
    SalesContractsWashedOutVolumeService washedOutVolumeService,
    SalesContractsChangeLogService changeLog,
    ContractNotificationOutboxService notificationOutbox,
    FinancialDocumentsAdjustProvisionalService provisionalAdjust,
    FinancialDocumentsGenerateWashoutReceivableService receivableGenerator)
{
    public async Task ExecuteAsync(Guid washoutKey, string? comments, string approvedBy)
    {
        if (comments != null && comments.Length > 500)
            throw new ApplicationException("O comentário deve ter no máximo 500 caracteres.");

        var washout = await context.SalesContractsWashouts
                          .Include(x => x.SalesContract).ThenInclude(c => c!.SalesShipmentReleases)
                          .Include(x => x.PriceFixation)
                          .FirstOrDefaultAsync(x => x.Key == washoutKey)
                      ?? throw new NotFoundException("Washout não encontrado.");

        if (washout.Status != SalesContractWashoutStatus.InApproval)
            throw new ApplicationException(
                $"Só é possível aprovar washout em aprovação. Status atual: {washout.Status}.");

        var contract = washout.SalesContract
                       ?? throw new NotFoundException("Contrato de venda não encontrado.");

        await SalesContractsWashoutGuard.ValidateAsync(context, contract, washout, washout.PriceFixation);

        // ANTES de qualquer mutação: o ajuste é também a trava V1 (provisório composto), e recusar aqui não deixa
        // nada rastreado para trás. Antes da transação também porque o gerador busca número por Dapper.
        if (washout.FixedVolume > 0 && washout.PriceFixation is { } fixation)
        {
            // O banco ainda não vê este washout como aprovado: soma os OUTROS e subtrai este.
            var otherApproved = await SalesContractsWashedOutVolumeService
                .ApprovedFixedVolumeAsync(context, fixation.Key, washout.Key);

            await provisionalAdjust.EnqueueForSalesFixationAsync(
                contract, fixation, fixation.FixationVolume - otherApproved - washout.FixedVolume, approvedBy);
        }

        var previous = ContractChangeLogFields.DescribeWashout(washout, contract.UnitOfMeasureCode);

        washout.Status = SalesContractWashoutStatus.Approved;
        washout.ApprovedBy = approvedBy;
        washout.ApprovedAt = DateTime.Now;
        washout.ApprovalComments = comments?.Trim();
        washout.UpdatedAt = DateTime.Now;
        washout.UpdatedBy = approvedBy;

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

        // Salva o status ANTES de recalcular (a soma lê o banco).
        await context.SaveChangesAsync();

        await washedOutVolumeService.RecalculateAsync(contract);
        await context.SaveChangesAsync();

        await transaction.CommitAsync();
    }
}
