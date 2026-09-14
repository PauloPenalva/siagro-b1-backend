using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.Financials;
using SiagroB1.Application.Services.Notifications;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.PurchaseContracts;

public class PurchaseContractsCloseService(
    AppDbContext context,
    PurchaseContractsFixedVolumeService fixedVolumeService,
    ContractNotificationOutboxService notificationOutbox,
    FinancialDocumentsCancelService financialDocuments)
{
    public async Task ExecuteAsync(Guid key, string userName)
    {
        var contract = await context.PurchaseContracts
                           .FirstOrDefaultAsync(x => x.Key == key && x.Status == ContractStatus.Approved)
                       ?? throw new NotFoundException("Contrato não encontrado ou não está aprovado.");

        // ANTES da guarda de fixação: ela desconta o washout lavado ASSUMINDO que só sobrou
        // Approved — se um InApproval ainda pudesse passar, a conta contaria volume que talvez
        // seja rejeitado e volte ao saldo.
        await GuardPendingWashoutsAsync(contract);

        if (contract.Type == ContractType.ToBeDetermined)
            await GuardPriceFixationAsync(contract);

        await GuardNegativeBalanceAsync(contract);

        contract.Status = ContractStatus.Finished;
        contract.UpdatedAt = DateTime.Now;
        contract.UpdatedBy = userName;

        notificationOutbox.Register(contract, NotificationEventType.Closed, userName);

        // Encerrar significa que não haverá mais entrega — e o provisório significa "saldo a
        // faturar". O que sobra nunca será faturado; deixá-lo aberto empilharia na tela um
        // "a faturar" permanente que envenena o total.
        await financialDocuments.EnqueueCancelByContractAsync(
            purchaseContractKey: contract.Key,
            salesContractKey: null,
            reason: "Contrato encerrado",
            userName: userName);

        await context.SaveChangesAsync();
    }

    /// <summary>
    /// Um contrato a fixar não pode ser encerrado devendo preço de mercadoria já entregue.
    /// Usa o volume CONFIRMADO — volume apenas em aprovação não define preço — descontado do
    /// volume lavado por washout: aquele volume fixado saiu do contrato e não precisa de preço.
    /// <see cref="GuardPendingWashoutsAsync"/> já rodou antes desta guarda, então os washouts
    /// ATIVOS aqui são só os APROVADOS.
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

        var (washedOutTotal, washedOutUnfixed) = await PurchaseContractsWashedOutVolumeService
            .ActiveVolumesAsync(context, contract.Key);
        var approvedWashedFixed = washedOutTotal - washedOutUnfixed;

        var pricedConfirmed = confirmedVolume - approvedWashedFixed;

        if (pricedConfirmed < deliveredVolume)
            throw new ApplicationException(
                $"Volume entregue sem preço fixado. Entregue: {deliveredVolume:N3}, " +
                $"fixado e confirmado (descontado o washout): {pricedConfirmed:N3}. " +
                "Fixe o preço do volume entregue antes de encerrar o contrato.");
    }

    /// <summary>
    /// Washout em aprovação reserva volume mas ainda pode ser rejeitado. Encerrar com ele pendente
    /// congelaria o contrato com um volume "lavado" que ninguém decidiu.
    /// </summary>
    private async Task GuardPendingWashoutsAsync(PurchaseContract contract)
    {
        var pendingCount = await context.PurchaseContractsWashouts
            .CountAsync(w => w.PurchaseContractKey == contract.Key
                             && w.Status == PurchaseContractWashoutStatus.InApproval);

        if (pendingCount > 0)
            throw new ApplicationException(
                $"Contrato possui {pendingCount} washout(s) pendente(s) de aprovação. " +
                "Aprove ou rejeite antes de encerrar.");
    }

    /// <summary>
    /// Espelha <c>SalesContractsCloseService</c>: contrato consumido ALÉM do volume contratado
    /// não pode ser congelado. Decide sobre o saldo RECALCULADO do ledger e dos washouts ativos,
    /// não sobre os agregados persistidos, que podem estar defasados. Não persiste o recálculo.
    /// </summary>
    private async Task GuardNegativeBalanceAsync(PurchaseContract contract)
    {
        var allocated = await PurchaseContractsRecalculateBalanceService
            .CalculateAllocatedAsync(context, contract.Key);
        var (washedOut, _) = await PurchaseContractsWashedOutVolumeService
            .ActiveVolumesAsync(context, contract.Key);
        var balance = decimal.Round(contract.TotalVolume - allocated - washedOut, 2, MidpointRounding.ToEven);

        if (balance < 0)
            throw new ApplicationException(
                $"Contrato faturado além do volume contratado. Contratado: {contract.TotalVolume:N2}, " +
                $"alocado: {allocated:N2}, washout: {washedOut:N2}, saldo: {balance:N2}. " +
                "Ajuste as alocações antes de encerrar.");
    }
}
