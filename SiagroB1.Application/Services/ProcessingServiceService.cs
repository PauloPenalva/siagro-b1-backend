using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Domain.Shared.Base;
using SiagroB1.Domain.Shared.Base.Shared.Base;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services;

public class ProcessingServiceService(AppDbContext context, ILogger<ProcessingServiceService> logger) 
    : BaseService<ProcessingService, string>(context, logger), IProcessingServiceService
{
}
