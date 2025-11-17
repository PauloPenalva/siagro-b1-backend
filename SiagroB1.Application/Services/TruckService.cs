using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Domain.Shared.Base.Shared.Base;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services;

public class TruckService(AppDbContext context, ILogger<TruckService> logger) : 
    BaseService<Truck, string>(context, logger), ITruckService
{
    
}