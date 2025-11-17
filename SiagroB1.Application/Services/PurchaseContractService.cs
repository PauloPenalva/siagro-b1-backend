using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Domain.Shared.Base.Shared.Base;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services;

public class PurchaseContractService(AppDbContext context, ILogger<IPurchaseContractService> logger) 
    : BaseService<PurchaseContract, Guid>(context, logger), IPurchaseContractService
{
    
}