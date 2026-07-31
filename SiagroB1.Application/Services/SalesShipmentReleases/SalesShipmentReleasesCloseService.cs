using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.SalesShipmentReleases;

public class SalesShipmentReleasesCloseService(
    AppDbContext context,
    SalesShipmentReleasesRecalculateShippedService recalcShipped)
{
    public async Task ExecuteAsync(Guid key, string userName)
    {
        var release = await context.SalesShipmentReleases
                          .FirstOrDefaultAsync(x => x.Key == key &&
                              (x.Status == ReleaseStatus.Actived || x.Status == ReleaseStatus.Paused))
                      ?? throw new NotFoundException("Liberação não encontrada ou não está ativa/pausada.");

        await GuardNegativeBalanceAsync(release);

        release.Status = ReleaseStatus.Completed;
        release.UpdatedAt = DateTime.Now;
        release.UpdatedBy = userName;

        await context.SaveChangesAsync();
    }

    /// <summary>
    /// Liberação faturada ALÉM do volume liberado não pode ser congelada: finalizada, ela sai
    /// do faturamento e do recálculo, e o volume excedente fica órfão. Saldo positivo finaliza
    /// normalmente — finalizar é abrir mão do que foi liberado e não embarcado; só o negativo
    /// barra. Espelha <c>SalesContractsCloseService.GuardNegativeBalanceAsync</c>: decide sobre
    /// o saldo RECALCULADO do ledger, não sobre <see cref="SalesShipmentRelease.ShippedQuantity"/>,
    /// que é persistido-derivado e pode estar defasado — usar o persistido barraria liberações
    /// corretas por drift. Não persiste o recálculo: finalizar continua sem efeito colateral de
    /// saldo (para isso existe a ação Recalcular Saldo).
    /// </summary>
    private async Task GuardNegativeBalanceAsync(SalesShipmentRelease release)
    {
        var shipped = await recalcShipped.CalculateShippedAsync(release.Key);
        var balance = SalesShipmentRelease.CalculateAvailableQuantity(release.ReleasedQuantity, shipped);

        if (balance < 0)
            throw new ApplicationException(
                $"Liberação faturada além do volume liberado. Liberado: {release.ReleasedQuantity:N3}, " +
                $"faturado: {shipped:N3}, saldo: {balance:N3}. " +
                "Regularize o excedente antes de finalizar a liberação.");
    }
}
