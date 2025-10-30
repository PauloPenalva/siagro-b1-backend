using Microsoft.Extensions.Logging;
using SiagroB1.Core.Base;
using SiagroB1.Core.Interfaces;
using SiagroB1.Domain.Entities;
using SiagroB1.Infra.Context;

namespace SiagroB1.Core.Services;

public class LoteArmazenagemService(AppDbContext context, ILogger<LoteArmazenagemService> logger) : 
    BaseService<LoteArmazenagem, int>(context, logger), ILoteArmazenagemService
{
    
}