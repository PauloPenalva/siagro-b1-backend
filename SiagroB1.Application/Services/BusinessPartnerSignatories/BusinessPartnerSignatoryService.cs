using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Shared.Base;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.BusinessPartnerSignatories;

public class BusinessPartnerSignatoryService(AppDbContext context, ILogger<BusinessPartnerSignatoryService> logger)
    : BaseService<BusinessPartnerSignatory, Guid>(context, logger)
{
}
