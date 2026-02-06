using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.PurchaseContracts;

public class PurchaseContractsCancelService(IUnitOfWork db)
{
    public async Task ExecuteAsync(Guid key, string? comments, string userName)
    {
        var contract = await db.Context.PurchaseContracts
                            .Include(x => x.Allocations)
                            .FirstOrDefaultAsync(x => x.Key == key 
                                                      && x.Status == ContractStatus.Approved) ?? 
                       throw new NotFoundException($"Contrato com a chave {key} não encontrado ou não está aprovado.");

        if (contract.Allocations.Any(x => 
                x.StorageTransaction?.TransactionStatus != StorageTransactionsStatus.Cancelled))
        {
            throw new ApplicationException("Contrato possui movimentos. Não é possivel cancelar, considere fazer washout.");
        }
        
        contract.Status = ContractStatus.Canceled;
        contract.ApprovalComments = comments;
        contract.CanceledAt = DateTime.Now;
        contract.CanceledBy = userName;

        await db.SaveChangesAsync();
    }
}