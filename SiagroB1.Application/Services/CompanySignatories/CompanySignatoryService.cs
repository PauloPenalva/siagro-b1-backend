using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Shared.Base;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.CompanySignatories;

public class CompanySignatoryService(AppDbContext context, ILogger<CompanySignatoryService> logger)
    : BaseService<CompanySignatory, Guid>(context, logger)
{
}
