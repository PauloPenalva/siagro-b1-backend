using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Domain.Shared.Base;
using SiagroB1.Domain.Shared.Base.Shared.Base;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services;

public class HarvestSeasonService(AppDbContext context, ILogger<HarvestSeasonService> logger) : 
    BaseService<HarvestSeason, string>(context, logger), IHarvestSeasonService
{
    
}