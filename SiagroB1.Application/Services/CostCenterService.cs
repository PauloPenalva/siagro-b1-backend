using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Domain.Models;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services;

/// <summary>
/// Centros de custo mantidos na tabela local COST_CENTERS (modo STANDALONE).
/// </summary>
public class CostCenterService(IUnitOfWork db, ILogger<CostCenterService> logger)
    : ICostCenterService
{
    public IQueryable<CostCenterModel> QueryAll()
    {
        return db.Context.CostCenters
            .Select(x => new CostCenterModel()
            {
                Code = x.Code,
                Name = x.Name,
                Inactive = x.Inactive,
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

    public async Task<IEnumerable<CostCenterModel>> GetAllAsync()
    {
        return await QueryAll().ToListAsync();
    }

    public async Task<CostCenterModel> CreateAsync(CostCenterModel entity)
    {
        if (await db.Context.CostCenters.AnyAsync(x => x.Code == entity.Code))
        {
            throw new DefaultException($"Centro de custo {entity.Code} já cadastrado.");
        }

        var costCenter = new CostCenter()
        {
            Code = entity.Code,
            Name = entity.Name,
            Inactive = entity.Inactive,
        };

        await db.Context.CostCenters.AddAsync(costCenter);
        await db.SaveChangesAsync();

        return entity;
    }

    public async Task<CostCenterModel?> UpdateAsync(string code, CostCenterModel entity)
    {
        var costCenter = await db.Context.CostCenters.FirstOrDefaultAsync(x => x.Code == code);

        if (costCenter == null)
        {
            return null;
        }

        costCenter.Name = entity.Name;
        costCenter.Inactive = entity.Inactive;

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            if (!EntityExists(code))
            {
                throw new KeyNotFoundException("Entity not found.");
            }

            throw new DefaultException("Error updating entity.");
        }

        return new CostCenterModel()
        {
            Code = costCenter.Code,
            Name = costCenter.Name,
            Inactive = costCenter.Inactive,
        };
    }

    public async Task<bool> DeleteAsync(string code)
    {
        var costCenter = await db.Context.CostCenters.FirstOrDefaultAsync(x => x.Code == code);

        if (costCenter == null)
        {
            return false;
        }

        db.Context.CostCenters.Remove(costCenter);
        await db.SaveChangesAsync();

        return true;
    }

    public Task<bool> DeleteAsyncWithTransaction(string code, Func<CostCenterModel, Task>? preDeleteAction = null)
    {
        throw new NotImplementedException();
    }

    private bool EntityExists(string code)
    {
        return db.Context.CostCenters.Any(x => x.Code == code);
    }
}
