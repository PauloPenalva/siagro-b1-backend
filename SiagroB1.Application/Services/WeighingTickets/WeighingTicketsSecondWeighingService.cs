using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.WeighingTickets;

public class WeighingTicketsSecondWeighingService(IUnitOfWork db)
{
    public async Task ExecuteAsync(Guid key, int weigh, string comments, string username)
    {
        if (weigh <= 0)
            throw new ApplicationException("Weigh value must be greater than zero");
        
        var ticket = await db.Context.WeighingTickets
            .Where(x => x.Stage == WeighingTicketStage.ReadyForSecondWeighing)
            .FirstOrDefaultAsync(x => x.Key == key) ??
                     throw new NotFoundException("Weighing ticket not found.");

        if (ticket.Stage != WeighingTicketStage.ReadyForSecondWeighing)
            throw new ApplicationException("Invalid ticket stage.");
        
        try
        {
            ticket.Status = WeighingTicketStatus.Processing;
            ticket.SecondWeighValue = weigh;
            ticket.SecondWeighDateTime = DateTime.Now;
            ticket.Stage = WeighingTicketStage.ReadyForCompleting;
            ticket.Comments = comments;
            ticket.SecondWeighUsername = username;
            
            await db.SaveChangesAsync();
        }
        catch (Exception e)
        {
            throw new ApplicationException(e.Message);
        }
    }
}