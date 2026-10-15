using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Notifications;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.SalesContracts;

/// <summary>
/// Registra um washout de venda em aprovação. Já RESERVA o volume (WashedOutVolume). Exposto como a OData action
/// <c>SalesContractsWashoutCreate</c>. Espelho de PurchaseContractsWashoutCreateService.
/// </summary>
public class SalesContractsWashoutCreateService(
    AppDbContext context,
    SalesContractsWashedOutVolumeService washedOutVolumeService,
    SalesContractsChangeLogService changeLog,
    ContractNotificationOutboxService notificationOutbox)
{
    public async Task<SalesContractWashout> ExecuteAsync(
        Guid salesContractKey, SalesContractWashout input, string userName)
    {
        var contract = await context.SalesContracts
                           .Include(x => x.SalesShipmentReleases)
                           .FirstOrDefaultAsync(x => x.Key == salesContractKey)
                       ?? throw new NotFoundException("Contrato de venda não encontrado.");

        var fixedVolume = decimal.Round(input.FixedVolume, 3, MidpointRounding.ToEven);

        // Washout só de volume não fixado não prende fixação: sem isso, bloquearia à toa o estorno da fixação.
        var fixation = fixedVolume > 0 && input.PriceFixationKey is { } fixationKey
            ? await context.SalesContractsPriceFixations.FirstOrDefaultAsync(x => x.Key == fixationKey)
              ?? throw new NotFoundException("Fixação de preço não encontrada.")
            : null;

        var washout = new SalesContractWashout
        {
            Key = Guid.NewGuid(),
            SalesContractKey = contract.Key,
            PriceFixationKey = fixation?.Key,
            FixedVolume = fixedVolume,
            UnfixedVolume = decimal.Round(input.UnfixedVolume, 3, MidpointRounding.ToEven),
            ContractPrice = fixation?.FixationPrice ?? 0m,
            MarketPrice = input.MarketPrice,
            PenaltyAmount = decimal.Round(input.PenaltyAmount, 2, MidpointRounding.ToEven),
            DueDate = input.DueDate?.Date,
            Reason = input.Reason?.Trim(),
            Status = SalesContractWashoutStatus.InApproval,
            CreatedAt = DateTime.Now,
            CreatedBy = userName,
            UpdatedAt = DateTime.Now,
            UpdatedBy = userName,
            ApprovedBy = null,
            CanceledBy = null,
        };

        washout.Amount = SalesContractWashout.CalculateAmount(
            washout.MarketPrice, washout.ContractPrice, washout.FixedVolume, washout.PenaltyAmount);

        await SalesContractsWashoutGuard.ValidateAsync(context, contract, washout, fixation);

        var lastSequence = await context.SalesContractsWashouts
            .Where(w => w.SalesContractKey == contract.Key)
            .MaxAsync(w => (int?)w.Sequence);
        washout.Sequence = (lastSequence ?? 0) + 1;

        context.SalesContractsWashouts.Add(washout);

        // Recalcula pelo banco (sem o novo) e soma o novo, que ainda só está rastreado. O RowVersion do contrato e o
        // índice único (contrato, sequência) barram o registro concorrente.
        await washedOutVolumeService.RecalculateAsync(contract);
        contract.WashedOutVolume += washout.FixedVolume + washout.UnfixedVolume;
        contract.WashedOutUnfixedVolume += washout.UnfixedVolume;

        changeLog.Register(
            contract.Key,
            ContractChangeLogFields.Washout,
            null,
            ContractChangeLogFields.DescribeWashout(washout, contract.UnitOfMeasureCode),
            userName);

        notificationOutbox.Register(contract, NotificationEventType.WashoutCreated, userName);

        await context.SaveChangesAsync();

        return washout;
    }
}
