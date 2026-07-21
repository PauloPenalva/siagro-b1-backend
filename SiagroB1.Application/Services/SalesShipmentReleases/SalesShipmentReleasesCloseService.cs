using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.SalesShipmentReleases;

public class SalesShipmentReleasesCloseService(AppDbContext context)
{
    public async Task ExecuteAsync(Guid key, string userName)
    {
        var release = await context.SalesShipmentReleases
                          .FirstOrDefaultAsync(x => x.Key == key &&
                              (x.Status == ReleaseStatus.Actived || x.Status == ReleaseStatus.Paused))
                      ?? throw new NotFoundException("Liberação não encontrada ou não está ativa/pausada.");

        release.Status = ReleaseStatus.Completed;
        release.UpdatedAt = DateTime.Now;
        release.UpdatedBy = userName;

        await context.SaveChangesAsync();
    }
}
