using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.PurchaseContracts;

public class PurchaseContractsRejectService(AppDbContext context)
{
    public async Task ExecuteAsync(Guid key, string? comments, string userName)
    {
        var contract = await context.PurchaseContracts
            .FirstOrDefaultAsync(x => x.Key == key && 
                x.Status == ContractStatus.InApproval) ?? 
                       throw new NotFoundException($"Contract with key {key} not found or not in approval.");
        
        contract.Status = ContractStatus.Rejected;
        contract.ApprovalComments = comments;
        contract.CanceledAt = DateTime.Now;
        contract.CanceledBy = userName;

        await context.SaveChangesAsync();
    }
}