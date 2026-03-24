using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using SiagroB1.Application.Services.StorageTransactions;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.WeighingTickets;

public class WeighingTicketsReOpenService(
    IUnitOfWork db,
    StorageTransactionsCancelService stCancelService,
    IStringLocalizer<Resource> resource)
{
    public async Task ExecuteAsync(Guid key, string userName)
    {
        
        var ticket = await db.Context.WeighingTickets
                         .FirstOrDefaultAsync(x => x.Key == key)
                     ?? throw new NotFoundException(resource["WEIGHING_TICKET_NOT_FOUND"]);
        
        if (ticket.Status is not WeighingTicketStatus.Complete)
            throw new BusinessException(resource["WEIGHING_TICKET_STATUS_NOT_COMPLETE"]);


        var st = await db.Context.StorageTransactions
            .AsNoTracking()
            .Where(x => x.WeighingTicketKey == key && 
                        x.TransactionStatus != StorageTransactionsStatus.Cancelled)
            .FirstOrDefaultAsync();
        
        if (st?.TransactionStatus is not StorageTransactionsStatus.Pending)
            throw new BusinessException(resource["WEIGHING_TICKET_REVERSE_STORAGE_TRANSACTION"] + st.Code);
        
        await db.BeginTransactionAsync();
        try
        {
            ticket.Status = WeighingTicketStatus.Processing;
            ticket.Stage = WeighingTicketStage.ReadyForCompleting;
            ticket.UpdatedBy = userName;
            ticket.UpdatedAt = DateTime.Now;
            ticket.FirstWeighDateTime =  DateTime.Now;
            ticket.SecondWeighDateTime =  DateTime.Now;
            ticket.FirstWeighUsername = userName + " (MANUAL)";
            ticket.SecondWeighUsername = userName + " (MANUAL)";
            
            await stCancelService.ExecuteAsync(st.Key, userName, TransactionCode.WeighingTicket);
            
            await db.SaveChangesAsync();
            await db.CommitAsync();
        }
        catch (Exception e)
        {
            await db.RollbackAsync();
            throw new BusinessException(e.Message);
        }
    }
}