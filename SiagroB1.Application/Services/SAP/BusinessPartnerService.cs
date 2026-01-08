using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities.SAP;
using SiagroB1.Domain.Interfaces.SAP;
using SiagroB1.Domain.Shared.Base;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.SAP;

public class BusinessPartnerService(SapErpDbContext context, ILogger<BusinessPartnerService> logger) 
    : BaseService<BusinessPartner, string>(context, logger), IBusinessPartnerService
{
    
}