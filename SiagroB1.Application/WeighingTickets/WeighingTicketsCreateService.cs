using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Application.DocNumbers;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Shared.Base.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.WeighingTickets;

public class WeighingTicketsCreateService(
    IUnitOfWork db, 
    DocNumberSequenceService numberSequenceService,
    ILogger<WeighingTicketsCreateService> logger)
{
    public async Task<WeighingTicket> ExecuteAsync(WeighingTicket entity, string userName)
    {
        entity.DocNumberKey ??= await numberSequenceService.GetKeyByTransactionCode(TransactionCode.WeighingTicket);
        
        try
        {
            entity.Code = await numberSequenceService.GetDocNumber((Guid) entity.DocNumberKey);
            entity.Date = DateTime.Now.Date;
            entity.Status = WeighingTicketStatus.Waiting;
            entity.FirstWeighValue = 0;
            entity.FirstWeighDateTime = null;
            entity.SecondWeighValue = 0;
            entity.SecondWeighDateTime = null;
            entity.Stage = WeighingTicketStage.ReadyForFirstWeighing;
            entity.CreatedAt = DateTime.Now;
            entity.CreatedBy = userName;
            
            var qualityAttribs = await GetQualityAttribs();
            
            qualityAttribs.ForEach(x =>
            {
                entity.QualityInspections.Add(new QualityInspection
                {
                    WeighingTicket =  entity,
                    QualityAttribCode = x.Code,
                    Value = 0,
                });
            });
            
            await db.Context.WeighingTickets.AddAsync(entity);
            await db.SaveChangesAsync();
            
            return entity;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error creating entity.");
            throw new DefaultException("Error creating entity.");
        }
    }

    private async Task<List<QualityAttrib>> GetQualityAttribs()
    {
        var qualityAttribs = await db.Context.QualityAttribs
            .AsNoTracking()
            .Where(x => !x.Disabled)
            .OrderBy(x => x.Code)
            .ToListAsync();
        return qualityAttribs;
    }
    
}