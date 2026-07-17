using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.ShipmentReleases;

public class ShipmentReleasesRecalculateBalanceService(
    AppDbContext context,
    ShipmentReleasesRecalculateShippedService recalcShipped)
{
    public async Task<ShipmentReleaseRecalcResultDto> ExecuteAsync(Guid key)
    {
        var release = await context.ShipmentReleases
                          .FirstOrDefaultAsync(x => x.Key == key)
                      ?? throw new NotFoundException("Liberação de embarque não encontrada.");

        if (release.Status == ReleaseStatus.Completed)
            throw new ApplicationException("Liberação finalizada não participa do recálculo de saldo.");

        return await RecalculateAsync(release);
    }

    public async Task<ShipmentReleaseRecalcAllResultDto> ExecuteAllAsync()
    {
        var releases = await context.ShipmentReleases
            .Where(x => x.Status != ReleaseStatus.Completed)
            .ToListAsync();

        var changes = new List<ShipmentReleaseRecalcResultDto>();
        foreach (var release in releases)
        {
            var result = await RecalculateAsync(release);
            if (result.Changed)
                changes.Add(result);
        }

        return new ShipmentReleaseRecalcAllResultDto
        {
            Scanned = releases.Count,
            Changed = changes.Count,
            Changes = changes,
        };
    }

    private async Task<ShipmentReleaseRecalcResultDto> RecalculateAsync(ShipmentRelease release)
    {
        var previousShipped = release.ShippedQuantity;
        var previousAvailable = release.AvailableQuantity;

        // recalcShipped usa o MESMO AppDbContext (scoped) → atualiza a mesma instância rastreada.
        await recalcShipped.RecalculateAsync(release.Key);

        return new ShipmentReleaseRecalcResultDto
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
