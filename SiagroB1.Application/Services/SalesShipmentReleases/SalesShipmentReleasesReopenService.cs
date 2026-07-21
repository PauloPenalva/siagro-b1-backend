using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.SalesShipmentReleases;

public class SalesShipmentReleasesReopenService(AppDbContext context)
{
    public async Task ExecuteAsync(Guid key, string userName)
    {
        var release = await context.SalesShipmentReleases
                          .FirstOrDefaultAsync(x => x.Key == key && x.Status == ReleaseStatus.Completed)
                      ?? throw new NotFoundException("Liberação não encontrada ou não está finalizada.");

        release.Status = ReleaseStatus.Actived;
        release.UpdatedAt = DateTime.Now;
        release.UpdatedBy = userName;

        await context.SaveChangesAsync();
    }
}
