using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Enums;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services.WeighingTickets;

public class WeighingTicketsSecondWeighingService(IUnitOfWork db, WeighingCaptureValidator validator)
{
    public async Task ExecuteAsync(Guid key, int weigh, string? comments, string username, Guid? captureId)
    {
        if (weigh <= 0)
            throw new ApplicationException("Quantidade deve ser maior que zero.");

        var ticket = await db.Context.WeighingTickets
            .Where(x => x.Stage == WeighingTicketStage.ReadyForSecondWeighing)
            .FirstOrDefaultAsync(x => x.Key == key) ??
                     throw new NotFoundException("Weighing ticket not found.");

        var origin = await validator.ResolveAsync(
            username, weigh, captureId, WeighingScalePurpose.Closing, ticket.TruckCode);

        ticket.Status = WeighingTicketStatus.Processing;
        ticket.SecondWeighValue = weigh;
        ticket.SecondWeighDateTime = DateTime.Now;
        ticket.Stage = WeighingTicketStage.ReadyForCompleting;
        ticket.Comments = comments;
        ticket.SecondWeighUsername = username;
        ticket.SecondWeighScaleCode = origin.ScaleCode;
        ticket.SecondWeighCaptured = origin.Captured;

        await db.SaveChangesAsync();
    }
}
