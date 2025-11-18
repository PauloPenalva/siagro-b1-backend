using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.PurchaseContracts;

public class PurchaseContractsSetRunnigStatusService(AppDbContext context)
{
    public async Task ExecuteAsync(Guid key)
    {
        var contract = await context.PurchaseContracts
            .FirstOrDefaultAsync(x => x.Key == key) ??
                       throw new NotFoundException($"Contract with key {key} not found");

        contract.Status = ContractStatus.Running;
        
        await context.SaveChangesAsync();
    }
}