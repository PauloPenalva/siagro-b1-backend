using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Application.Services.StorageDeposits;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.ShipmentReleases;

public class ShipmentReleasesDeleteService(IUnitOfWork db, ILogger<ShipmentReleasesDeleteService> logger)
{
    public async Task<bool> ExecuteAsync(Guid key)
    {
        var entity = await db.Context.ShipmentReleases
            .FirstOrDefaultAsync(x => x.Key == key) ??
                     throw new NotFoundException("Shipment Release not found.");

        var contract = await db.Context.PurchaseContracts
            .FirstOrDefaultAsync(x => x.Key == entity.PurchaseContractKey);

        if (contract?.Status == ContractStatus.Finished)
            throw new ApplicationException("Contrato encerrado: não é possível excluir a liberação de embarque.");

        if (entity.Status != ReleaseStatus.Pending)
        {
            throw new ApplicationException("Shipment Release not pending.");
        }

        await StorageDepositRules.EnsureNoActiveRemittanceAsync(db.Context, entity.Key, "excluir");

        // Remessa cancelada continua referenciando a liberação (FK Restrict): sem esta recusa seria 500.
        if (await db.Context.SalesInvoices.AnyAsync(i => i.ShipmentReleaseKey == key))
            throw new ApplicationException(
                "A liberação tem remessa para depósito emitida e não pode ser excluída. Cancele a liberação.");

        if (await db.Context.WarehouseReconciliationReleases.AnyAsync(x => x.ShipmentReleaseKey == key))
            throw new ApplicationException("Liberação usada em conferência de saldo de armazém: não pode ser excluída.");

        db.Context.ShipmentReleases.Remove(entity);
        await db.SaveChangesAsync();
        return true;
    }
}