using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Enums;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.SalesShipmentReleases;

public class SalesShipmentReleasesRecalculateShippedService(AppDbContext context)
{
    /// <summary>
    /// Tipos de romaneio que consomem o saldo da liberação de VENDA. Fonte única da regra —
    /// os hooks do faturamento consultam este predicado em vez de repetir a lista.
    /// (Devolução de venda NÃO cria romaneio novo; vira o status da <c>SalesShipment</c>
    /// original para <c>Returned</c>, que sai da soma — restaurando o saldo.)
    /// </summary>
    public static bool AffectsShippedQuantity(StorageTransactionType type) =>
        type is StorageTransactionType.SalesShipment;

    /// <summary>
    /// Calcula o volume consumido da liberação SEM persistir nada, para quem precisa
    /// decidir antes de gravar (ex.: cancelamento, que recusa quando não há saldo).
    /// Fonte: ledger SALES_CONTRACTS_ALLOCATIONS — Σ Volume assinado por liberação
    /// (nominal, sem fator de quebra: quebra de entrega não devolve saldo à liberação).
    /// Reproduz os comportamentos do faturamento sem hooks extras: devolução confirma
    /// linhas negativas na mesma liberação de origem (soma volta ao pré-faturamento),
    /// cancelamento deleta as linhas (soma zera) e realocação devolve saldo à liberação
    /// de origem (linha −) enquanto consome a do destino (linha +).
    /// </summary>
    public async Task<decimal> CalculateShippedAsync(Guid salesShipmentReleaseKey)
    {
        return await context.SalesContractsAllocations
            .Where(a => a.SalesShipmentReleaseKey == salesShipmentReleaseKey)
            .SumAsync(a => a.Volume);
    }

    public async Task RecalculateAsync(Guid salesShipmentReleaseKey)
    {
        var release = await context.SalesShipmentReleases
            .FirstOrDefaultAsync(x => x.Key == salesShipmentReleaseKey);

        if (release is null)
            return;

        release.ShippedQuantity = await CalculateShippedAsync(salesShipmentReleaseKey);

        await context.SaveChangesAsync();
    }
}
