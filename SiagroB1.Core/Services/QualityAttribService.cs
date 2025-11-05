using Microsoft.Extensions.Logging;
using SiagroB1.Core.Base;
using SiagroB1.Core.Interfaces;
using SiagroB1.Domain.Entities;
using SiagroB1.Infra.Context;

namespace SiagroB1.Core.Services
{
    public class QualityAttribService : BaseService<QualityAttrib, string>, IQualityAttribService
    {

        public QualityAttribService(AppDbContext context, ILogger<QualityAttribService> logger) : base(context, logger)
        {
        }
        
    }
}