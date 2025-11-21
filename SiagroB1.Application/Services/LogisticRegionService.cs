using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Shared.Base.Shared.Base;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services;

public class LogisticRegionService(AppDbContext context, ILogger<LogisticRegionService> logger) 
    : BaseService<LogisticRegion, string>(context, logger)
{
    
}