using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.WeighingTickets;

public class WeighingTicketsQualityInspectionsCreateService(
    AppDbContext context, ILogger<WeighingTicketsQualityInspectionsCreateService> logger)
{
    public async Task<QualityInspection> ExecuteAsync(Guid ticketKey, QualityInspection associationEntity)
    {
        try
        {
            var existingEntity = await context.WeighingTickets.FindAsync(ticketKey)
                ?? throw new NotFoundException("Storage transaction not found");
            
            associationEntity.WeighingTicket = existingEntity;
            await context.AddAsync(associationEntity);
            
            await context.SaveChangesAsync();
            
            return associationEntity;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, exception.Message);
            throw;
        }
    }
}