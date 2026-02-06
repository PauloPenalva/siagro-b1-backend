using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.ShipmentReleases;

public class ShipmentReleasesApprovationService(IUnitOfWork db, ILogger<ShipmentReleasesApprovationService> logger)
{
    public async Task ExecuteAsync(Guid key, string userName)
    {
        var sr = await db.Context.ShipmentReleases
                     .Include(x => x.PurchaseContract)
                     .ThenInclude(p => p.ShipmentReleases)
                     .FirstOrDefaultAsync(x => x.Key == key) ??
                 throw new NotFoundException($"Shipment Release not found key {key}");
        
        if (sr.Status is ReleaseStatus.Actived or ReleaseStatus.Cancelled or ReleaseStatus.Completed)
        {
            throw new ArgumentException("Shipment Release not pending ou paused.");
        }

        if (sr.PurchaseContract == null)
        { 
            throw new NotFoundException($"Purchase Contract not found key {sr.PurchaseContractKey}");
        }
        
        if (sr.PurchaseContract.Status != ContractStatus.Approved)
        {
            // contrato de compra não está aprovado.
            throw new ArgumentException("Purchase Contract not approved.");
        }
       
        // se a quantidade liberada é superior ao total disponivel do contrato
        // lança a exceção
        if (sr.Status != ReleaseStatus.Paused && sr.ReleasedQuantity > sr.PurchaseContract.TotalAvailableToReleaseWithoutProvisioning)
        {
            throw new ArgumentException("Quantity is higher than the total available to release.");
        }
        
        try
        {
            sr.Status = ReleaseStatus.Actived;
            sr.ApprovedBy = userName;
            sr.ApprovedAt = DateTime.Now;

            await db.SaveChangesAsync();
        }
        catch(Exception e)
        {
            throw new ApplicationException(e.Message);
        }
    }
    
}