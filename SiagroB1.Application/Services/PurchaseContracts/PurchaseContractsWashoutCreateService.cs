using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Notifications;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.PurchaseContracts;

/// <summary>
/// Registra um washout em aprovação. Já RESERVA o volume (WashedOutVolume), para que duas pessoas
/// não lavem a mesma tonelagem enquanto a decisão não sai. Exposto como a OData action
/// <c>PurchaseContractsWashoutCreate</c>.
/// </summary>
public class PurchaseContractsWashoutCreateService(
    AppDbContext context,
    PurchaseContractsWashedOutVolumeService washedOutVolumeService,
    PurchaseContractsChangeLogService changeLog,
    ContractNotificationOutboxService notificationOutbox)
{
    public async Task<PurchaseContractWashout> ExecuteAsync(
        Guid purchaseContractKey, PurchaseContractWashout input, string userName)
    {
        var contract = await context.PurchaseContracts
                           .Include(x => x.ShipmentReleases)
                           .FirstOrDefaultAsync(x => x.Key == purchaseContractKey)
                       ?? throw new NotFoundException("Contrato de compra não encontrado.");

        var fixedVolume = decimal.Round(input.FixedVolume, 3, MidpointRounding.ToEven);

        // Washout só de volume não fixado não prende fixação: sem isso, ele bloquearia à toa o
        // estorno da fixação escolhida na tela.
        var fixation = fixedVolume > 0 && input.PriceFixationKey is { } fixationKey
            ? await context.PurchaseContractsPriceFixations.FirstOrDefaultAsync(x => x.Key == fixationKey)
              ?? throw new NotFoundException("Fixação de preço não encontrada.")
            : null;

        var washout = new PurchaseContractWashout
        {
            Key = Guid.NewGuid(),
            PurchaseContractKey = contract.Key,
            PriceFixationKey = fixation?.Key,
            FixedVolume = fixedVolume,
            UnfixedVolume = decimal.Round(input.UnfixedVolume, 3, MidpointRounding.ToEven),
            ContractPrice = fixation?.FixationPrice ?? 0m,
            MarketPrice = input.MarketPrice,
            PenaltyAmount = decimal.Round(input.PenaltyAmount, 2, MidpointRounding.ToEven),
            DueDate = input.DueDate?.Date,
            Reason = input.Reason?.Trim(),
            Status = PurchaseContractWashoutStatus.InApproval,
            CreatedAt = DateTime.Now,
            CreatedBy = userName,
            UpdatedAt = DateTime.Now,
            UpdatedBy = userName,
            ApprovedBy = null,
            CanceledBy = null,
        };

        washout.Amount = PurchaseContractWashout.CalculateAmount(
            washout.MarketPrice, washout.ContractPrice, washout.FixedVolume, washout.PenaltyAmount);

        await PurchaseContractsWashoutGuard.ValidateAsync(context, contract, washout, fixation);

        var lastSequence = await context.PurchaseContractsWashouts
            .Where(w => w.PurchaseContractKey == contract.Key)
            .MaxAsync(w => (int?)w.Sequence);
        washout.Sequence = (lastSequence ?? 0) + 1;

        context.PurchaseContractsWashouts.Add(washout);

        // Recalcula pelo banco (sem o novo) e soma o novo, que ainda só está rastreado. O RowVersion
        // do contrato e o índice único (contrato, sequência) barram o registro concorrente.
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
