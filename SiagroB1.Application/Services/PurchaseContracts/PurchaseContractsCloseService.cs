using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.PurchaseContracts;

public class PurchaseContractsCloseService(AppDbContext context)
{
    public async Task ExecuteAsync(Guid key, string userName)
    {
        var contract = await context.PurchaseContracts
                           .FirstOrDefaultAsync(x => x.Key == key && x.Status == ContractStatus.Approved)
                       ?? throw new NotFoundException("Contrato não encontrado ou não está aprovado.");

        contract.Status = ContractStatus.Finished;
        contract.UpdatedAt = DateTime.Now;
        contract.UpdatedBy = userName;

        await context.SaveChangesAsync();
    }
}
