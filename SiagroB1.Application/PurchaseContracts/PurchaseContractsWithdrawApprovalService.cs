using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.PurchaseContracts;

public class PurchaseContractsWithdrawApprovalService(AppDbContext db)
{
    public async Task ExecuteAsync(Guid key, string userName)
    {
        var contract = await db.PurchaseContracts
            .FirstOrDefaultAsync(x => x.Key == key) ??
                       throw new NotFoundException("Purchase contract not found.");

        if (contract.Status != ContractStatus.InApproval)
        {
            throw new ApplicationException("Purchase contract not in approval.");
        }

        contract.Status = ContractStatus.Draft;
        contract.UpdatedBy = userName;
        contract.UpdatedAt = DateTime.Now;
        await db.SaveChangesAsync();
    }
}