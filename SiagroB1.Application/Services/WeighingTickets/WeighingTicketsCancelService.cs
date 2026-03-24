using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using SiagroB1.Application.Services.StorageTransactions;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.WeighingTickets;

public class WeighingTicketsCancelService(
    IUnitOfWork db,
    WeighingTicketsGetService weighingTicketsGetService,
    StorageTransactionsCancelService storageTransactionsCancelService,
    IStringLocalizer<Resource> resource)
{
    public async Task ExecuteAsync(Guid key, string userName)
    {
        var ticket = await weighingTicketsGetService.GetByIdAsync(key) 
            ?? throw new KeyNotFoundException(resource["WEIGHING_TICKET_NOT_FOUND"]);

        var sa = await GetStorageTransactionByWeighingTicketKey(key);
        if (sa is { TransactionStatus: not StorageTransactionsStatus.Pending })
        {
            throw new BusinessException(resource["STORAGE_TRANSACTION_NOT_PENDING"]);
        }
        
        try
        {
            await db.BeginTransactionAsync();
            
            if (sa is not null)
                await storageTransactionsCancelService
                    .ExecuteAsync((Guid) sa.Key, userName, TransactionCode.WeighingTicket);
           
            ticket.Status = WeighingTicketStatus.Cancelled;
            ticket.Stage = WeighingTicketStage.Completed;
            
            await db.SaveChangesAsync();
            await db.CommitAsync();
        }
        catch (Exception e)
        {
            await db.RollbackAsync();
            throw new ApplicationException(e.Message);
        }
    }

    private async Task<StorageTransaction?> GetStorageTransactionByWeighingTicketKey(Guid key)
    {
        return await db.Context.StorageTransactions
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.WeighingTicketKey == key);
    }
}