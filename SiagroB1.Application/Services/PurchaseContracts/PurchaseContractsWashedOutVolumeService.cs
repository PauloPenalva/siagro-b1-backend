using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.PurchaseContracts;

/// <summary>
/// Ponto ÚNICO das somas de washout. <see cref="RecalculateAsync"/> é o único escritor de
/// <see cref="PurchaseContract.WashedOutVolume"/> e <see cref="PurchaseContract.WashedOutUnfixedVolume"/>.
///
/// Os métodos estáticos existem para quem não pode ganhar dependência no construtor sem quebrar
/// dezenas de testes (encerramento, gerador de provisório) — mesmo idioma de
/// PurchaseContractsRecalculateBalanceService.CalculateAllocatedAsync.
///
/// Todas as somas leem o BANCO: mudança só rastreada não entra. Quem muda status salva antes.
/// </summary>
public class PurchaseContractsWashedOutVolumeService(AppDbContext context)
{
    /// <summary>Recalcula e atribui os dois agregados. NÃO persiste.</summary>
    public async Task RecalculateAsync(PurchaseContract contract)
    {
        var (total, unfixed) = await ActiveVolumesAsync(context, contract.Key);
        contract.WashedOutVolume = total;
        contract.WashedOutUnfixedVolume = unfixed;
    }

    /// <summary>Σ dos washouts ATIVOS (InApproval + Approved) do contrato.</summary>
    public static async Task<(decimal Total, decimal Unfixed)> ActiveVolumesAsync(
        AppDbContext context, Guid contractKey, Guid? excludingWashoutKey = null)
    {
        var rows = await context.PurchaseContractsWashouts
            .Where(w => w.PurchaseContractKey == contractKey
                        && (w.Status == PurchaseContractWashoutStatus.InApproval
                            || w.Status == PurchaseContractWashoutStatus.Approved)
                        && (excludingWashoutKey == null || w.Key != excludingWashoutKey))
            .Select(w => new { w.FixedVolume, w.UnfixedVolume })
            .ToListAsync();

        return (Round(rows.Sum(r => r.FixedVolume + r.UnfixedVolume)), Round(rows.Sum(r => r.UnfixedVolume)));
    }

    /// <summary>Volume fixado já reservado por washouts ATIVOS de uma fixação (trava de saldo).</summary>
    public static Task<decimal> ActiveFixedVolumeAsync(
        AppDbContext context, Guid fixationKey, Guid? excludingWashoutKey = null) =>
        SumFixedAsync(context, fixationKey, excludingWashoutKey, onlyApproved: false);

    /// <summary>Volume fixado lavado por washouts APROVADOS de uma fixação (valor do provisório).</summary>
    public static Task<decimal> ApprovedFixedVolumeAsync(
        AppDbContext context, Guid fixationKey, Guid? excludingWashoutKey = null) =>
        SumFixedAsync(context, fixationKey, excludingWashoutKey, onlyApproved: true);

    private static async Task<decimal> SumFixedAsync(
        AppDbContext context, Guid fixationKey, Guid? excludingWashoutKey, bool onlyApproved)
    {
        var total = await context.PurchaseContractsWashouts
            .Where(w => w.PriceFixationKey == fixationKey
                        && (w.Status == PurchaseContractWashoutStatus.Approved
                            || (!onlyApproved && w.Status == PurchaseContractWashoutStatus.InApproval))
                        && (excludingWashoutKey == null || w.Key != excludingWashoutKey))
            .SumAsync(w => w.FixedVolume);

        return Round(total);
    }

    private static decimal Round(decimal value) => decimal.Round(value, 3, MidpointRounding.ToEven);
}
