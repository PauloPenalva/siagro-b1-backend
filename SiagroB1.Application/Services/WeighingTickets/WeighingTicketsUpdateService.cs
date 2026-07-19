using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.WeighingTickets;

public class WeighingTicketsUpdateService(
    IUnitOfWork db,
    IBusinessPartnerService businessPartnerService,
    IItemService itemService,
    ILogger<WeighingTicketsUpdateService> logger)
{
    public async Task<WeighingTicket?> ExecuteAsync(Guid key, WeighingTicket entity, string userName)
    {
        var existingEntity = await db.Context.WeighingTickets
                                 .FirstOrDefaultAsync(tc => tc.Key == key) ?? 
                             throw new KeyNotFoundException("Entity not found.");

        if (existingEntity.Status == WeighingTicketStatus.Complete)
        {
            throw new ApplicationException("Weighing ticket is in complete status.");
        }
        
        try
        {
            db.Context.Entry(existingEntity).CurrentValues.SetValues(entity);

            // Depois do SetValues e em `existingEntity`: as colunas desnormalizadas
            // precisam acompanhar a troca do código, senão a tela mostra o nome antigo
            // ao lado do código novo na próxima leitura.
            existingEntity.CardName = (await businessPartnerService.GetByIdAsync(entity.CardCode))?.CardName;
            existingEntity.ItemName = (await itemService.GetByIdAsync(entity.ItemCode))?.ItemName;

            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            logger.Log(LogLevel.Error, "Failed to update entity.");
            throw new ApplicationException("Error updating entity due to concurrency issues.");
        }

        return entity;
    }
}