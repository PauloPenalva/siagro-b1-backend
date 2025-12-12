using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Application.DocTypes;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Shared.Base.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.WeighingTickets;

public class WeighingTicketsCreateService(
    AppDbContext context, 
    DocTypesService docTypesService,
    ILogger<WeighingTicketsCreateService> logger)
{
    public async Task<WeighingTicket> ExecuteAsync(WeighingTicket entity, string userName)
    {
        
        await using var transaction = await context.Database.BeginTransactionAsync();
        try
        {
            var currentNumber = await docTypesService.GetNextNumber(entity.DocTypeCode, TransactionCode.WeighingTicket);
            
            entity.Code = FormatCode(currentNumber);
            entity.Date = DateTime.Now.Date;
            entity.Status = WeighingTicketStatus.Waiting;
            entity.FirstWeighValue = 0;
            entity.FirstWeighDateTime = null;
            entity.SecondWeighValue = 0;
            entity.SecondWeighDateTime = null;
            entity.Stage = WeighingTicketStage.ReadyForFirstWeighing;
            
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
            
            await context.WeighingTickets.AddAsync(entity);
            await docTypesService.UpdateLastNumber(entity.DocTypeCode, currentNumber, TransactionCode.WeighingTicket);
            await context.SaveChangesAsync();
            await transaction.CommitAsync();
            
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
        var qualityAttribs = await context.QualityAttribs
            .AsNoTracking()
            .Where(x => !x.Disabled)
            .OrderBy(x => x.Code)
            .ToListAsync();
        return qualityAttribs;
    }

    private static string FormatCode(int currentCode)
    {
        return currentCode.ToString()
            .PadLeft(10, '0');
    }
}