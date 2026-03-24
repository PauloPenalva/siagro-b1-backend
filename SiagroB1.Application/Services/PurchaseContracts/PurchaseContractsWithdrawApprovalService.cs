using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.PurchaseContracts;

public class PurchaseContractsWithdrawApprovalService(
    IUnitOfWork db,
    IStringLocalizer<Resource> resource
    )
{
    public async Task ExecuteAsync(Guid key, string userName)
    {
        var contract = await db.Context.PurchaseContracts
                           .Include(x => x.ShipmentReleases)
                           .FirstOrDefaultAsync(x => x.Key == key) ??
                              throw new NotFoundException("Purchase contract not found.");

        if (contract.Status != ContractStatus.InApproval)
            throw new ApplicationException(resource["PURCHASE_CONTRACT_NOT_IN_APPROVAL_STATUS"]);
        
        if (contract.HasShipmentReleases)
            throw new BusinessException(resource["PURCHASE_CONTRACT_HAS_SHIPMENT_RELEASES"]);
        
        contract.Status = ContractStatus.Draft;
        contract.UpdatedBy = userName;
        contract.UpdatedAt = DateTime.Now;
        await db.SaveChangesAsync();
    }
}