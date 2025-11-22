using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.WeighingTickets;

public class WeighingTicketsUpdateService(AppDbContext context, ILogger<WeighingTicketsUpdateService> logger)
{
    public Task<WeighingTicket?> ExecuteAsync(Guid key, WeighingTicket entity, string userName)
    {
        throw new NotImplementedException();
    }
}