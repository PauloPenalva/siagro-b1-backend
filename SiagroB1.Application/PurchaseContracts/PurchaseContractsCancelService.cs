using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.PurchaseContracts;

public class PurchaseContractsCancelService(AppDbContext context)
{
    public async Task Cancel(Guid key, string userName)
    {
        var contract = await context.PurchaseContracts
            .FirstOrDefaultAsync(x => x.Key == key && 
                x.Status == ContractStatus.Approved) ?? 
                       throw new NotFoundException($"Contract with key {key} not found or not approved.");
        
        contract.Status = ContractStatus.Canceled;
        contract.CanceledAt = DateTime.Now;
        contract.CanceledBy = userName;

        await context.SaveChangesAsync();
    }
}