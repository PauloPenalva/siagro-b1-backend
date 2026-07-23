using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.SalesContracts;

/// <summary>
/// Ponto ÚNICO de recálculo de <see cref="SalesContract.FixedVolume"/>. Espelha
/// <c>PurchaseContractsFixedVolumeService</c>. Todo serviço que cria, aprova, rejeita,
/// cancela, edita ou apaga uma fixação de venda deve chamar <see cref="RecalculateAsync"/> —
/// nunca replicar a soma.
/// </summary>
public class SalesContractsFixedVolumeService(AppDbContext context)
{
    /// <summary>
    /// Recalcula e atribui <see cref="SalesContract.FixedVolume"/> (Σ InApproval + Confirmed).
    /// NÃO persiste — o chamador é dono da transação e do SaveChanges.
    /// </summary>
    public async Task<decimal> RecalculateAsync(SalesContract contract)
    {
        var total = await context.SalesContractsPriceFixations
            .Where(f => f.SalesContractKey == contract.Key
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
        var total = await context.SalesContractsPriceFixations
            .Where(f => f.SalesContractKey == contractKey
                        && f.Status == PriceFixationStatus.Confirmed)
            .SumAsync(f => f.FixationVolume);

        return decimal.Round(total, 3, MidpointRounding.ToEven);
    }

    /// <summary>
    /// Volume FISICAMENTE entregue: Σ <see cref="SalesShipmentRelease.ShippedQuantity"/>.
    /// </summary>
    /// <remarks>
    /// NÃO usar <c>SalesContract.TotalShipmentReleases</c> aqui — aquele computado soma
    /// <c>ConsumedQuantity</c> (= <c>ReleasedQuantity</c> numa liberação ativa), ou seja o
    /// volume LIBERADO, não o romaneado. Consulta direta ao banco evita a dependência de Include.
    /// </remarks>
    public async Task<decimal> DeliveredVolumeAsync(Guid contractKey)
    {
        var total = await context.SalesShipmentReleases
            .Where(r => r.SalesContractKey == contractKey)
            .SumAsync(r => r.ShippedQuantity);

        return decimal.Round(total, 3, MidpointRounding.ToEven);
    }

    /// <summary>
    /// Preço unitário fixado do contrato: média ponderada por volume das fixações
    /// CONFIRMADAS (Σ FixationPrice×FixationVolume / Σ FixationVolume). É a fonte do preço
    /// snapshotado no ledger de alocações (<c>SalesContractsAllocationCreateService</c>).
    /// </summary>
    /// <param name="fallbackPrice">
    /// Preço a usar quando ainda não há fixação confirmada. Para contrato de preço fixo é
    /// o próprio <c>SalesContract.Price</c> (que a fixação automática espelha), e para PAF
    /// sem fixação confirmada é 0 (o preço só passa a existir quando a diretoria confirma).
    /// Assim o snapshot de contratos fixos permanece idêntico ao comportamento anterior.
    /// </param>
    public async Task<decimal> ConfirmedUnitPriceAsync(Guid contractKey, decimal fallbackPrice)
    {
        var rows = await context.SalesContractsPriceFixations
            .Where(f => f.SalesContractKey == contractKey
                        && f.Status == PriceFixationStatus.Confirmed)
            .Select(f => new { f.FixationPrice, f.FixationVolume })
            .ToListAsync();

        var volume = rows.Sum(r => r.FixationVolume);
        if (volume == 0m)
            return fallbackPrice;

        var weighted = rows.Sum(r => r.FixationPrice * r.FixationVolume);
        return decimal.Round(weighted / volume, 8, MidpointRounding.ToEven);
    }
}
