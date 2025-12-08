using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.SalesContracts;

public class SalesContractsWithdrawApprovalService(AppDbContext db)
{
    public async Task ExecuteAsync(Guid key, string userName)
    {
        var contract = await db.SalesContracts
            .FirstOrDefaultAsync(x => x.Key == key) ??
                       throw new NotFoundException("Sales contract not found.");

        if (contract.Status != ContractStatus.InApproval)
        {
            throw new ApplicationException("Sales contract not in approval.");
        }

        contract.Status = ContractStatus.Draft;
        contract.UpdatedBy = userName;
        contract.UpdatedAt = DateTime.Now;
        await db.SaveChangesAsync();
    }
}