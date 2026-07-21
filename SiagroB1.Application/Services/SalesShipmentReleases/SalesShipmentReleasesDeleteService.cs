using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.SalesShipmentReleases;

public class SalesShipmentReleasesDeleteService(IUnitOfWork db, ILogger<SalesShipmentReleasesDeleteService> logger)
{
    public async Task<bool> ExecuteAsync(Guid key)
    {
        var entity = await db.Context.SalesShipmentReleases
            .FirstOrDefaultAsync(x => x.Key == key) ??
                     throw new NotFoundException("Sales Shipment Release not found.");

        var contract = await db.Context.SalesContracts
            .FirstOrDefaultAsync(x => x.Key == entity.SalesContractKey);

        if (contract?.Status == ContractStatus.Finished)
            throw new ApplicationException("Contrato encerrado: não é possível excluir a liberação de entrega.");

        if (entity.Status != ReleaseStatus.Pending)
        {
            throw new ApplicationException("Sales Shipment Release not pending.");
        }

        db.Context.SalesShipmentReleases.Remove(entity);
        await db.SaveChangesAsync();
        return true;
    }
}
