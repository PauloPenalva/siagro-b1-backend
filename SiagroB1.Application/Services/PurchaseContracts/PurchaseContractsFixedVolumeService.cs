using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.PurchaseContracts;

/// <summary>
/// Ponto ÚNICO de recálculo de <see cref="PurchaseContract.FixedVolume"/>.
/// Todo serviço que cria, aprova, rejeita, cancela, edita ou apaga uma fixação
/// deve chamar <see cref="RecalculateAsync"/> — nunca replicar a soma.
/// </summary>
public class PurchaseContractsFixedVolumeService(AppDbContext context)
{
    /// <summary>
    /// Recalcula e atribui <see cref="PurchaseContract.FixedVolume"/>.
    /// NÃO persiste — o chamador é dono da transação e do SaveChanges.
    /// </summary>
    public async Task<decimal> RecalculateAsync(PurchaseContract contract)
    {
        var total = await context.PurchaseContractsPriceFixations
            .Where(f => f.PurchaseContractKey == contract.Key
                        && (f.Status == PriceFixationStatus.InApproval
                            || f.Status == PriceFixationStatus.Confirmed))
            .SumAsync(f => f.FixationVolume);

        contract.FixedVolume = decimal.Round(total, 3, MidpointRounding.ToEven);
        return contract.FixedVolume;
    }

    /// <summary>
    /// Σ dos volumes de fixações CONFIRMADAS. Usado pela guarda de fechamento,
    /// que não pode aceitar volume apenas em aprovação.
    /// </summary>
    public async Task<decimal> ConfirmedVolumeAsync(Guid contractKey)
    {
        var total = await context.PurchaseContractsPriceFixations
            .Where(f => f.PurchaseContractKey == contractKey
                        && f.Status == PriceFixationStatus.Confirmed)
            .SumAsync(f => f.FixationVolume);

        return decimal.Round(total, 3, MidpointRounding.ToEven);
    }

    /// <summary>
    /// Volume FISICAMENTE entregue: Σ <see cref="ShipmentRelease.ShippedQuantity"/>.
    /// </summary>
    /// <remarks>
    /// NÃO usar <c>PurchaseContract.TotalShipmentReleases</c> aqui. Aquele computado soma
    /// <c>ConsumedQuantity</c>, que numa liberação não cancelada vale <c>ReleasedQuantity</c> —
    /// isto é, o volume LIBERADO, não o romaneado. Uma liberação ativa de 60.000 kg com apenas
    /// 10.000 kg romaneados contaria 60.000 e bloquearia o fechamento por mercadoria que ainda
    /// não chegou. Consulta direta ao banco também evita a dependência de Include.
    /// </remarks>
    public async Task<decimal> DeliveredVolumeAsync(Guid contractKey)
    {
        var total = await context.ShipmentReleases
            .Where(r => r.PurchaseContractKey == contractKey)
            .SumAsync(r => r.ShippedQuantity);

        return decimal.Round(total, 3, MidpointRounding.ToEven);
    }
}
