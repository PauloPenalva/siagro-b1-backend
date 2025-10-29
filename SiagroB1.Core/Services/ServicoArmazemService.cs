using Microsoft.Extensions.Logging;
using SiagroB1.Core.Base;
using SiagroB1.Domain.Entities;
using SiagroB1.Infra.Context;

namespace SiagroB1.Core.Services
{
    public class ServicoArmazemService(AppDbContext context, ILogger<ServicoArmazemService> logger) : BaseService<ServicoArmazem, int>(context, logger), IServicoArmazemService
    {
    }
}