using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Domain.Models;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.SAP;

/// <summary>
/// Centros de custo lidos de OPRC. O cadastro é mantido no SAP: as operações de
/// escrita não são suportadas neste modo.
/// </summary>
public class CostCenterService(SapErpDbContext context, ILogger<CostCenterService> logger)
    : ICostCenterService
{
    public IQueryable<CostCenterModel> QueryAll()
    {
        return context.CostCenters
            .Where(x => x.Active == "Y")
            .Select(x => new CostCenterModel()
            {
                Code = x.Code,
                Name = x.Name,
                Inactive = false,
            })
            .AsNoTracking();
    }

    public async Task<CostCenterModel?> GetByIdAsync(string code)
    {
        try
        {
            return await QueryAll().FirstOrDefaultAsync(x => x.Code == code);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error fetching entity with ID {Id}", code);
            throw new DefaultException("Error fetching entity");
        }
    }

    public Task<IEnumerable<CostCenterModel>> GetAllAsync()
    {
        throw new NotImplementedException("Not implemented on SAP context.");
    }

    public Task<CostCenterModel> CreateAsync(CostCenterModel entity)
    {
        throw new NotImplementedException("Not implemented on SAP context.");
    }

    public Task<CostCenterModel?> UpdateAsync(string code, CostCenterModel entity)
    {
        throw new NotImplementedException("Not implemented on SAP context.");
    }

    public Task<bool> DeleteAsync(string code)
    {
        throw new NotImplementedException("Not implemented on SAP context.");
    }

    public Task<bool> DeleteAsyncWithTransaction(string code, Func<CostCenterModel, Task>? preDeleteAction = null)
    {
        throw new NotImplementedException("Not implemented on SAP context.");
    }
}
