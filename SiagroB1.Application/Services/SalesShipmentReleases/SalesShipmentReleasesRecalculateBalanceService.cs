using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.SalesShipmentReleases;

public class SalesShipmentReleasesRecalculateBalanceService(
    AppDbContext context,
    SalesShipmentReleasesRecalculateShippedService recalcShipped)
{
    public async Task<SalesShipmentReleaseRecalcResultDto> ExecuteAsync(Guid key)
    {
        var release = await context.SalesShipmentReleases
                          .FirstOrDefaultAsync(x => x.Key == key)
                      ?? throw new NotFoundException("Liberação de entrega não encontrada.");

        if (release.Status == ReleaseStatus.Completed)
            throw new ApplicationException("Liberação finalizada não participa do recálculo de saldo.");

        return await RecalculateAsync(release);
    }

    public async Task<SalesShipmentReleaseRecalcAllResultDto> ExecuteAllAsync()
    {
        var releases = await context.SalesShipmentReleases
            .Where(x => x.Status != ReleaseStatus.Completed)
            .ToListAsync();

        var changes = new List<SalesShipmentReleaseRecalcResultDto>();
        foreach (var release in releases)
        {
            var result = await RecalculateAsync(release);
            if (result.Changed)
                changes.Add(result);
        }

        return new SalesShipmentReleaseRecalcAllResultDto
        {
            Scanned = releases.Count,
            Changed = changes.Count,
            Changes = changes,
        };
    }

    private async Task<SalesShipmentReleaseRecalcResultDto> RecalculateAsync(SalesShipmentRelease release)
    {
        var previousShipped = release.ShippedQuantity;
        var previousAvailable = release.AvailableQuantity;

        // recalcShipped usa o MESMO AppDbContext (scoped) → atualiza a mesma instância rastreada.
        await recalcShipped.RecalculateAsync(release.Key);

        return new SalesShipmentReleaseRecalcResultDto
        {
            Key = release.Key,
            PreviousShippedQuantity = previousShipped,
            NewShippedQuantity = release.ShippedQuantity,
            PreviousAvailableQuantity = previousAvailable,
            NewAvailableQuantity = release.AvailableQuantity,
            Changed = previousShipped != release.ShippedQuantity,
        };
    }
}
