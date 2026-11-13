using Microsoft.EntityFrameworkCore;
using SiagroB1.Application.Services.PurchaseContracts;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.ShipmentReleases;

public class ShipmentReleasesReopenService(AppDbContext context)
{
    public async Task ExecuteAsync(Guid key, string userName)
    {
        var release = await context.ShipmentReleases
                          .FirstOrDefaultAsync(x => x.Key == key && x.Status == ReleaseStatus.Completed)
                      ?? throw new NotFoundException("Liberação não encontrada ou não está finalizada.");

        ShipmentReleaseOwnershipTransferGuard.Ensure(release);

        release.Status = ReleaseStatus.Actived;
        release.UpdatedAt = DateTime.Now;
        release.UpdatedBy = userName;

        // GAC-1214: liberação de devolução viva devolve saldo ao contrato; finalizada, não.
        // Status e sincronização do contrato gravam na mesma transação.
        await PurchaseContractsReturnReleaseAllocationSync.SaveAndSyncAsync(context, release);
    }
}
