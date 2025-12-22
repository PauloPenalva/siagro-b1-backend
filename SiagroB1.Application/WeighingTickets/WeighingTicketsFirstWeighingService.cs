using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.WeighingTickets;

public class WeighingTicketsFirstWeighingService(IUnitOfWork db)
{
    public async Task ExecuteAsync(Guid key, int weigh)
    {
        if (weigh <= 0)
        {
            throw new ApplicationException("Weigh value must be greater than zero");
        }
        
        var ticket = await db.Context.WeighingTickets
            .Where(x => x.Stage == WeighingTicketStage.ReadyForFirstWeighing)
            .FirstOrDefaultAsync(x => x.Key == key) ??
                     throw new NotFoundException("Weighing ticket not found.");

        if (ticket.Stage != WeighingTicketStage.ReadyForFirstWeighing)
        {
            throw new ApplicationException("Invalid ticket stage.");
        }

        ticket.Status = WeighingTicketStatus.Processing;
        ticket.FirstWeighValue = weigh;
        ticket.FirstWeighDateTime = DateTimeOffset.Now;
        ticket.Stage = WeighingTicketStage.ReadyForSecondWeighing;

        try
        {
            await db.SaveChangesAsync();
        }
        catch (Exception e)
        {
            throw new ApplicationException(e.Message);
        }
    }
}