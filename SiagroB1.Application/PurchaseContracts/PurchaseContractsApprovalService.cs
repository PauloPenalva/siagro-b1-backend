using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.PurchaseContracts;

public class PurchaseContractsApprovalService(AppDbContext context)
{
    public async Task Approval(Guid key, string approvedBy)
    {
        var contract = await context.PurchaseContracts
            .FirstOrDefaultAsync(x => x.Key == key && x.Status == ContractStatus.Draft)  ?? 
                       throw new NotFoundException($"Contract with key {key} not found or not draft.");
        
        contract.Status = ContractStatus.Approved;
        contract.ApprovedAt = DateTime.Now;
        contract.ApprovedBy = approvedBy;

        await context.SaveChangesAsync();
    }
}