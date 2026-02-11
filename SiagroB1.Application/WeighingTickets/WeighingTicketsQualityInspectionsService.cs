using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.WeighingTickets;

public class WeighingTicketsQualityInspectionsService(
    IUnitOfWork db,
    WeighingTicketsGetService getService
    )
{
    public async Task ExecuteAsync(Guid key, List<WeighingTicketsQualityInspectionsDto> inspections)
    {
        var ticket = await getService.GetByIdAsync(key) ??
                     throw new NotFoundException($"Ticket with key {key} not found.");

        if (ticket.Status == WeighingTicketStatus.Complete)
        {
            throw new  ApplicationException("Ticket is complete.");
        }
        
        try
        {
            await db.BeginTransactionAsync();

            inspections.ForEach(inspection =>
            {
                var exisitingInspection = db.Context.QualityInspections
                    .FirstOrDefault(x => x.Key == inspection.Key);

                if (exisitingInspection == null)
                {
                    AddQualityInspection(ticket, inspection);
                }
                else
                {
                    UpdateQualityInspection(exisitingInspection, inspection);
                }
                
            });
            await db.SaveChangesAsync();
            await db.CommitAsync();
        }
        catch (Exception e)
        {
            await db.RollbackAsync();
            throw new ApplicationException(e.Message);
        }
    }

    private void UpdateQualityInspection(QualityInspection exisitingInspection,
        WeighingTicketsQualityInspectionsDto inspection)
    {
        db.Context.Entry(exisitingInspection).State = EntityState.Modified;
        exisitingInspection.Value = inspection.Value;
    }

    private void AddQualityInspection(WeighingTicket ticket, WeighingTicketsQualityInspectionsDto inspection)
    {
        db.Context.QualityInspections.Add(new QualityInspection
        {
            WeighingTicketKey = ticket.Key,
            QualityAttribCode = inspection.QualityAttribCode,
            Value = inspection.Value
        });
    }
}