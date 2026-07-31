using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Notifications;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.PurchaseContracts;

public class PurchaseContractsCloseService(
    AppDbContext context,
    PurchaseContractsFixedVolumeService fixedVolumeService,
    ContractNotificationOutboxService notificationOutbox)
{
    public async Task ExecuteAsync(Guid key, string userName)
    {
        var contract = await context.PurchaseContracts
                           .FirstOrDefaultAsync(x => x.Key == key && x.Status == ContractStatus.Approved)
                       ?? throw new NotFoundException("Contrato não encontrado ou não está aprovado.");

        if (contract.Type == ContractType.ToBeDetermined)
            await GuardPriceFixationAsync(contract);

        await GuardNegativeBalanceAsync(contract);

        contract.Status = ContractStatus.Finished;
        contract.UpdatedAt = DateTime.Now;
        contract.UpdatedBy = userName;

        notificationOutbox.Register(contract, NotificationEventType.Closed, userName);

        await context.SaveChangesAsync();
    }

    /// <summary>
    /// Um contrato a fixar não pode ser encerrado devendo preço de mercadoria já entregue.
    /// Usa o volume CONFIRMADO — volume apenas em aprovação não define preço.
    /// </summary>
    private async Task GuardPriceFixationAsync(PurchaseContract contract)
    {
        var pendingCount = await context.PurchaseContractsPriceFixations
            .CountAsync(f => f.PurchaseContractKey == contract.Key
                             && f.Status == PriceFixationStatus.InApproval);

        if (pendingCount > 0)
            throw new ApplicationException(
                $"Contrato possui {pendingCount} fixação(ões) pendente(s) de aprovação. " +
                "Aprove ou rejeite antes de encerrar.");

        var confirmedVolume = await fixedVolumeService.ConfirmedVolumeAsync(contract.Key);
        var deliveredVolume = await fixedVolumeService.DeliveredVolumeAsync(contract.Key);

        if (confirmedVolume < deliveredVolume)
            throw new ApplicationException(
                $"Volume entregue sem preço fixado. Entregue: {deliveredVolume:N3}, " +
                $"fixado e confirmado: {confirmedVolume:N3}. " +
                "Fixe o preço do volume entregue antes de encerrar o contrato.");
    }

    /// <summary>
    /// Espelha <c>SalesContractsCloseService</c>: contrato consumido ALÉM do volume contratado
    /// não pode ser congelado — encerrado, ele sai das listas de alocação e do recálculo em
    /// lote, e o volume excedente fica órfão. Decide sobre o saldo RECALCULADO do ledger, não
    /// sobre <see cref="PurchaseContract.AllocatedVolume"/>: o agregado é persistido-derivado e
    /// pode estar defasado — usar o valor persistido barraria contratos corretos por drift.
    /// Não persiste o recálculo.
    /// </summary>
    private async Task GuardNegativeBalanceAsync(PurchaseContract contract)
    {
        var allocated = await PurchaseContractsRecalculateBalanceService
            .CalculateAllocatedAsync(context, contract.Key);
        var balance = decimal.Round(contract.TotalVolume - allocated, 2, MidpointRounding.ToEven);

        if (balance < 0)
            throw new ApplicationException(
                $"Contrato faturado além do volume contratado. Contratado: {contract.TotalVolume:N2}, " +
                $"alocado: {allocated:N2}, saldo: {balance:N2}. " +
                "Ajuste as alocações antes de encerrar.");
    }
}
