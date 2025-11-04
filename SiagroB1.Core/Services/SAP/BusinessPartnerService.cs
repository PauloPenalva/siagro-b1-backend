using Microsoft.Extensions.Logging;
using SiagroB1.Core.Base;
using SiagroB1.Core.Interfaces.SAP;
using SiagroB1.Domain.Entities.SAP;
using SiagroB1.Infra.Context;

namespace SiagroB1.Core.Services.SAP;

public class BusinessPartnerService(SapErpDbContext context, ILogger<BusinessPartnerService> logger) 
    : BaseService<BusinessPartner, string>(context, logger), IBusinessPartnerService
{
    
}