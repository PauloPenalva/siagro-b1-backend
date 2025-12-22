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
    DocNumbersSequenceService docNumbersSequenceService,
    ILogger<WeighingTicketsCreateService> logger)
{
    public async Task<WeighingTicket> ExecuteAsync(WeighingTicket entity, string userName)
    {
        
        if (entity.DocNumberKey is null)
        {
            var docNumbers = await docNumbersSequenceService.GetDocNumbersSeries(TransactionCode.SalesInvoice);
            var docNumber = docNumbers.FirstOrDefault(x => x.Default);
            if (docNumber == null)
                throw new ApplicationException("Document Number is empty or not setting default value.");

            entity.DocNumberKey = docNumber.Key;
        }
        
        try
        {
            await db.BeginTransactionAsync();
            
            var docNumber = await docNumbersSequenceService.GetDocNumber((Guid) entity.DocNumberKey);

            var currentNumber = docNumber.NextNumber;
            
            entity.Code = DocNumbersSequenceService
                .FormatNumber(currentNumber, int.Parse(docNumber.NumberSize), docNumber.Prefix, docNumber.Suffix);
            
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
            
            await db.Context.WeighingTickets.AddAsync(entity);
            await docNumbersSequenceService.UpdateLastNumber((Guid) entity.DocNumberKey, currentNumber);
            await db.SaveChangesAsync();
            await db.CommitAsync();
            
            return entity;
        }
        catch (Exception ex)
        {
            await db.RollbackAsync();
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