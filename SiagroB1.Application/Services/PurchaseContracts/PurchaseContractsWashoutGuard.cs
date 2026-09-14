using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.PurchaseContracts;

/// <summary>
/// Travas do washout, compartilhadas pela criação e pela aprovação (que revalida com o estado de
/// agora: entre o registro e a decisão o contrato pode ter recebido alocação, liberação ou outro
/// washout). Todas as somas EXCLUEM o próprio washout, então o mesmo código serve aos dois.
///
/// Exige o contrato carregado com <c>ShipmentReleases</c> (saldo a liberar) e
/// <c>washout.Amount</c> já calculado.
/// </summary>
internal static class PurchaseContractsWashoutGuard
{
    public static async Task ValidateAsync(
        AppDbContext context,
        PurchaseContract contract,
        PurchaseContractWashout washout,
        PurchaseContractPriceFixation? fixation)
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

        // Movido para cima do bloco de FixedVolume: a trava F2 (volume com preço ainda não
        // entregue) precisa do washout ATIVO já lavado, e o resto da função reaproveita o mesmo
        // par em vez de consultar de novo.
        var (activeTotal, activeUnfixed) = await PurchaseContractsWashedOutVolumeService
            .ActiveVolumesAsync(context, contract.Key, washout.Key);

        if (washout.FixedVolume > 0)
        {
            if (fixation is null)
                throw new ApplicationException("Informe a fixação de preço do volume fixado.");

            if (fixation.PurchaseContractKey != contract.Key)
                throw new ApplicationException("A fixação informada não pertence a este contrato.");

            if (fixation.Status != PriceFixationStatus.Confirmed)
                throw new ApplicationException("A fixação informada não está confirmada.");

            var washedFromFixation = await PurchaseContractsWashedOutVolumeService
                .ActiveFixedVolumeAsync(context, fixation.Key, washout.Key);
            var fixationBalance = fixation.FixationVolume - washedFromFixation;

            if (washout.FixedVolume > fixationBalance)
                throw new ApplicationException(
                    $"Volume fixado excede o saldo da fixação. Disponível: {fixationBalance:N3}, " +
                    $"solicitado: {washout.FixedVolume:N3}.");

            // F2 (revisão final): um washout cobre só o volume NÃO entregue/liberado. Sem esta
            // trava, lavar volume fixado já ENTREGUE some com o preço e o título a pagar daquela
            // mercadoria, mesmo com o físico e a fixação com saldo. Vale para os dois tipos de
            // contrato: no preço fixo a fixação automática cobre o total contratado, então a
            // conta é inofensiva.
            var fixedVolumeService = new PurchaseContractsFixedVolumeService(context);
            var confirmedVolume = await fixedVolumeService.ConfirmedVolumeAsync(contract.Key);
            var deliveredVolume = await fixedVolumeService.DeliveredVolumeAsync(contract.Key);
            var washedFixed = activeTotal - activeUnfixed;
            var pricedUndelivered = confirmedVolume - deliveredVolume - washedFixed;

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
