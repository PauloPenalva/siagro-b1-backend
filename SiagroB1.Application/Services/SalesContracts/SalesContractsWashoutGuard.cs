using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.SalesContracts;

/// <summary>
/// Travas do washout de venda, compartilhadas pela criação e pela aprovação (que revalida com o estado de agora).
/// Todas as somas EXCLUEM o próprio washout. Espelho de PurchaseContractsWashoutGuard, com uma diferença (spec
/// 2026-10-15, §5.1): na venda a entrega é o faturamento, então o "entregue" é o alocado no ledger.
///
/// Exige o contrato carregado com <c>SalesShipmentReleases</c> (saldo a liberar) e <c>washout.Amount</c> calculado.
/// </summary>
internal static class SalesContractsWashoutGuard
{
    public static async Task ValidateAsync(
        AppDbContext context,
        SalesContract contract,
        SalesContractWashout washout,
        SalesContractPriceFixation? fixation)
    {
        if (contract.Status != ContractStatus.Approved)
            throw new ApplicationException("Contrato precisa estar aprovado para movimentar washout.");

        if (washout.FixedVolume < 0 || washout.UnfixedVolume < 0)
            throw new ApplicationException("Os volumes do washout não podem ser negativos.");

        var total = washout.FixedVolume + washout.UnfixedVolume;
        if (total <= 0)
            throw new ApplicationException("Informe o volume do washout.");

        if (washout.MarketPrice < 0)
            throw new ApplicationException("O preço de mercado não pode ser negativo.");

        if (washout.PenaltyAmount < 0)
            throw new ApplicationException("A multa não pode ser negativa.");

        if (string.IsNullOrWhiteSpace(washout.Reason))
            throw new ApplicationException("Informe o motivo do washout.");

        if (washout.Reason.Length > 500)
            throw new ApplicationException("O motivo do washout deve ter no máximo 500 caracteres.");

        if (contract.Type != ContractType.ToBeDetermined && washout.UnfixedVolume > 0)
            throw new ApplicationException(
                "Contrato de preço fixo não tem volume a fixar: informe só o volume fixado.");

        var (activeTotal, activeUnfixed) = await SalesContractsWashedOutVolumeService
            .ActiveVolumesAsync(context, contract.Key, washout.Key);

        if (washout.FixedVolume > 0)
        {
            if (fixation is null)
                throw new ApplicationException("Informe a fixação de preço do volume fixado.");

            if (fixation.SalesContractKey != contract.Key)
                throw new ApplicationException("A fixação informada não pertence a este contrato.");

            if (fixation.Status != PriceFixationStatus.Confirmed)
                throw new ApplicationException("A fixação informada não está confirmada.");

            var washedFromFixation = await SalesContractsWashedOutVolumeService
                .ActiveFixedVolumeAsync(context, fixation.Key, washout.Key);
            var fixationBalance = fixation.FixationVolume - washedFromFixation;

            if (washout.FixedVolume > fixationBalance)
                throw new ApplicationException(
                    $"Volume fixado excede o saldo da fixação. Disponível: {fixationBalance:N3}, " +
                    $"solicitado: {washout.FixedVolume:N3}.");

            // Volume fixado ainda não entregue = confirmado − faturado (ledger RECALCULADO, não o AllocatedVolume
            // persistido) − lavado fixado ativo. Lavar volume já faturado sumiria com o preço daquela mercadoria.
            var confirmedVolume = await new SalesContractsFixedVolumeService(context).ConfirmedVolumeAsync(contract.Key);
            var billedVolume = await SalesContractsRecalculateBalanceService.CalculateAllocatedAsync(context, contract.Key);
            var washedFixed = activeTotal - activeUnfixed;
            var pricedUndelivered = confirmedVolume - billedVolume - washedFixed;

            if (washout.FixedVolume > pricedUndelivered)
                throw new ApplicationException(
                    $"Volume fixado excede o volume com preço ainda não entregue. Disponível: {pricedUndelivered:N3}, " +
                    $"solicitado: {washout.FixedVolume:N3}.");
        }

        if (washout.UnfixedVolume > 0)
        {
            var availableToPricing = contract.TotalVolume - contract.FixedVolume - activeUnfixed;
            if (washout.UnfixedVolume > availableToPricing)
                throw new ApplicationException(
                    $"Volume não fixado excede o saldo a fixar. Disponível: {availableToPricing:N3}, " +
                    $"solicitado: {washout.UnfixedVolume:N3}.");
        }

        var physical = decimal.Round(contract.TotalVolume - contract.AllocatedVolume - activeTotal, 3, MidpointRounding.ToEven);
        if (total > physical)
            throw new ApplicationException(
                $"Volume do washout excede o saldo físico do contrato. Disponível: {physical:N3}, " +
                $"solicitado: {total:N3}.");

        var toRelease = decimal.Round(contract.TotalVolume - contract.TotalShipmentReleases - activeTotal, 3, MidpointRounding.ToEven);
        if (total > toRelease)
            throw new ApplicationException(
                $"Volume do washout excede o saldo não liberado do contrato. Disponível: {toRelease:N3}, " +
                $"solicitado: {total:N3}. Encerre ou cancele a liberação antes.");

        if (washout.Amount > 0 && washout.DueDate is null)
            throw new ApplicationException("Informe o vencimento do título a receber.");
    }
}
