using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Domain.Models;
using SiagroB1.Domain.Shared.Base.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.SAP;

public class UnitOfMeasureService(SapErpDbContext context, ILogger<UnitOfMeasureService> logger) 
    : IUnitOfMeasureService
{
     public Task<UnitOfMeasureModel> CreateAsync(UnitOfMeasureModel model)
     {
         throw new NotImplementedException("Not implemented on SAP context.");
     }

    public Task<bool> DeleteAsync(string code)
    {
        throw new NotImplementedException("Not implemented on SAP context.");
    }

    public IQueryable<UnitOfMeasureModel> QueryAll()
    {
        return context.UnitsOfMeasure
            .AsNoTracking()
            .Select(x => new UnitOfMeasureModel
            {
                Code = x.Code,
                Description = x.Description,
                Locked =  x.Locked ?? "N",
            });
    }

    public async Task<IEnumerable<UnitOfMeasureModel>> GetAllAsync()
    {
        return await context.UnitsOfMeasure
            .Select(x => new UnitOfMeasureModel
            {
                Code = x.Code,
                Description = x.Description,
                Locked =  x.Locked ?? "N",
            })
            .ToListAsync();
    }

    public async Task<UnitOfMeasureModel?> GetByIdAsync(string code)
    {
        try
        {
            return await context.UnitsOfMeasure
                .Select(x => new UnitOfMeasureModel
                {
                    Code = x.Code,
                    Description = x.Description,
                    Locked =  x.Locked ?? "N",
                })
                .FirstOrDefaultAsync(x => x.Code == code);
                
        }
        catch (Exception ex)
        {
            throw new DefaultException("Error fetching entity");
        }
    }
    
    public async Task<UnitOfMeasureModel?> UpdateAsync(string key, UnitOfMeasureModel model)
    {
        throw new NotImplementedException("Not implemented on SAP context.");
    }
    
}
