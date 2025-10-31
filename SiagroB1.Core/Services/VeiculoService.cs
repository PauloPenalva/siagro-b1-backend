using Microsoft.Extensions.Logging;
using SiagroB1.Core.Base;
using SiagroB1.Core.Interfaces;
using SiagroB1.Domain.Entities;
using SiagroB1.Infra.Context;

namespace SiagroB1.Core.Services;

public class VeiculoService(AppDbContext context, ILogger<VeiculoService> logger) : 
    BaseService<Veiculo, int>(context, logger), IVeiculoService
{
    
}