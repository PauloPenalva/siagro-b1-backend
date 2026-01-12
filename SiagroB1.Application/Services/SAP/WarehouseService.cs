using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Domain.Models;
using SiagroB1.Domain.Shared.Base.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.SAP;

public class WarehouseService(
    SapErpDbContext context, 
    ILogger<WarehouseService> logger,
    IConfiguration configuration
    ) 
    : IWarehouseService
{
    public Task<WarehouseModel> CreateAsync(WarehouseModel model)
    {
        throw new NotImplementedException("Not implemented on SAP context.");
    }

    public Task<bool> DeleteAsync(string code)
    {
        throw new NotImplementedException("Not implemented on SAP context.");
    }

    public IQueryable<WarehouseModel> QueryAll()
    {
        return context.BusinessPartners
            .AsNoTracking()
            .Where(x => x.QryGroup23 == "Y")
            .Select(x => new WarehouseModel
            {
                Code = x.CardCode,
                Name = x.CardName,
                TaxId =  x.TaxId,
            });
    }

    public async Task<IEnumerable<WarehouseModel>> GetAllAsync()
    {
        return await context.BusinessPartners
            .Where(x => x.QryGroup23 == "Y")
            .Select(x => new WarehouseModel
            {
                Code = x.CardCode,
                Name = x.CardName,
                TaxId =  x.TaxId,
            })
            .ToListAsync();
    }

    public async Task<WarehouseModel?> GetByIdAsync(string code)
    {
        try
        {
            return await context.BusinessPartners
                .Where(x => x.QryGroup23 == "Y")
                .Select(x => new WarehouseModel
                {
                    Code = x.CardCode,
                    Name = x.CardName,
                    TaxId =  x.TaxId,
                })
                .FirstOrDefaultAsync(x => x.Code == code);
                
        }
        catch (Exception ex)
        {
            throw new DefaultException("Error fetching entity");
        }
    }
    
    public async Task<WarehouseModel?> UpdateAsync(string key, WarehouseModel model)
    {
        throw new NotImplementedException("Not implemented on SAP context.");
    }
}