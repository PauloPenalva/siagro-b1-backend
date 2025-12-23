using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities.SAP;
using SiagroB1.Domain.Interfaces.SAP;
using SiagroB1.Domain.Shared.Base;
using SiagroB1.Domain.Shared.Base.Shared.Base;

using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.SAP;

public class ItemService(SapErpDbContext context, ILogger<ItemService> logger) 
    : BaseService<Item, string>(context, logger), IItemService
{
    
}