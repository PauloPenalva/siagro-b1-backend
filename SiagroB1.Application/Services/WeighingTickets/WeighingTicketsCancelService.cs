using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using SiagroB1.Application.Interfaces;
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
    IUserPermissions permissions,
    IStringLocalizer<Resource> resource)
{
    public async Task ExecuteAsync(Guid key, string userName)
    {
        var ticket = await weighingTicketsGetService.GetByIdAsync(key) 
            ?? throw new KeyNotFoundException(resource["WEIGHING_TICKET_NOT_FOUND"].Value);

        if (ticket.Status == WeighingTicketStatus.Cancelled)
            throw new BusinessException("Ticket de pesagem já está cancelado.");

        // Ticket em andamento ainda não gerou romaneio: não há o que estornar, só o ticket muda.
        // Fica restrito ao papel ADMIN do perfil (HasRoleAsync não tem o bypass do flag legado).
        if (ticket.Status is WeighingTicketStatus.Waiting or WeighingTicketStatus.Processing)
        {
            if (!await permissions.HasRoleAsync(userName, Domain.Constants.Roles.Admin))
                throw new BusinessException(
                    "Somente usuário com o papel ADMIN pode cancelar um ticket de pesagem não concluído.");

            ticket.Status = WeighingTicketStatus.Cancelled;
            ticket.Stage = WeighingTicketStage.Completed;
            ticket.UpdatedBy = userName;
            ticket.UpdatedAt = DateTime.Now;

            await db.SaveChangesAsync();
            return;
        }

        var sa = await GetStorageTransactionByWeighingTicketKey(key);
        if (sa == null)
            throw new NotFoundException(resource["STORAGE_TRANSACTION_NOT_FOUND"].Value);
        
        if (sa.TransactionStatus != StorageTransactionsStatus.Pending )
        {
            throw new BusinessException(resource["STORAGE_TRANSACTION_NOT_PENDING"].Value);
        }
        
        try
        {
            await db.BeginTransactionAsync();
            
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