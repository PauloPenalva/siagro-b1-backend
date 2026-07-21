using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.SalesContracts;

public class SalesContractsReopenService(AppDbContext context)
{
    public async Task ExecuteAsync(Guid key, string userName)
    {
        var contract = await context.SalesContracts
                           .FirstOrDefaultAsync(x => x.Key == key && x.Status == ContractStatus.Finished)
                       ?? throw new NotFoundException("Contrato não encontrado ou não está encerrado.");

        contract.Status = ContractStatus.Approved;
        contract.UpdatedAt = DateTime.Now;
        contract.UpdatedBy = userName;

        await context.SaveChangesAsync();
    }
}
