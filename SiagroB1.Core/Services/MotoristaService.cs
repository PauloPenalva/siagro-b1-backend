using Microsoft.Extensions.Logging;
using SiagroB1.Core.Base;
using SiagroB1.Core.Interfaces;
using SiagroB1.Domain.Entities;
using SiagroB1.Infra.Context;

namespace SiagroB1.Core.Services;

public class MotoristaService(AppDbContext context, ILogger<MotoristaService> logger) : 
    BaseService<Motorista, int>(context, logger), IMotoristaService
{
    
}