using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.WeighingTickets;

public class WeighingTicketsFirstWeighingService(IUnitOfWork db)
{
    public async Task ExecuteAsync(Guid key, int weigh, string comments, string username)
    {
        if (weigh <= 0)
            throw new ApplicationException("Quantidade deve ser maior que zero.");
        
        var ticket = await db.Context.WeighingTickets
            .Where(x => x.Stage == WeighingTicketStage.ReadyForFirstWeighing)
            .FirstOrDefaultAsync(x => x.Key == key) ??
                     throw new NotFoundException("Weighing ticket not found.");

        if (ticket.Stage != WeighingTicketStage.ReadyForFirstWeighing)
            throw new ApplicationException("Ticket stage inválido.");
        
        try
        {
            ticket.Status = WeighingTicketStatus.Processing;
            ticket.FirstWeighValue = weigh;
            ticket.FirstWeighDateTime = DateTime.Now;
            ticket.Stage = WeighingTicketStage.ReadyForSecondWeighing;
            ticket.Comments = comments;
            ticket.FirstWeighUsername =  username;
            
            await db.SaveChangesAsync();
        }
        catch (Exception e)
        {
            throw new ApplicationException(e.Message);
        }
    }
}