using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Shared.Base.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.WeighingTickets;

public class WeighingTicketsCreateService(AppDbContext context, ILogger<WeighingTicketsCreateService> logger)
{
    public async Task<WeighingTicket> ExecuteAsync(WeighingTicket entity, string userName)
    {
        
        await using var transaction = await context.Database.BeginTransactionAsync();
        try
        {
            entity.Date = DateTime.Now;
            entity.Status = WeighingTicketStatus.Waiting;
            entity.FirstWeighValue = 0;
            entity.FirstWeighDateTime = null;
            entity.SecondWeighValue = 0;
            entity.SecondWeighDateTime = null;
            entity.Stage = WeighingTicketStage.ReadyForFirstWeighing;
            
            await context.WeighingTickets.AddAsync(entity);
            await context.SaveChangesAsync();
            
            var qualityAttribs = await context.QualityAttribs
                .AsNoTracking()
                .Where(x => !x.Disabled)
                .OrderBy(x => x.Code)
                .ToListAsync();
            
            qualityAttribs.ForEach(x =>
            {
                entity.QualityInspections.Add(new QualityInspection
                {
                    WeighingTicketKey = entity.Key,
                    QualityAttribCode = x.Code,
                    Value = 0,
                });
                
                context.SaveChanges();
            });
            
            await transaction.CommitAsync();
            return entity;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error creating entity.");
            throw new DefaultException("Error creating entity.");
        }
    }    
}