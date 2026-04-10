using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Domain.Models;
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
            .Include(x => x.Addresses)
            .AsNoTracking()
            .Where(x => x.QryGroup23 == "Y")
            .Select(x => new WarehouseModel
            {
                Code = x.CardCode,
                Name = x.CardName,
                TaxId =  x.TaxId,
                FName = x.CardFName,
                Notes = x.Notes,
                City = x.Addresses.ToList().FirstOrDefault().City,
                State = x.Addresses.ToList().FirstOrDefault().State,
            });
    }

    public async Task<Dictionary<string, WarehouseInfo>> LoadWarehousesAsync()
    {
        return await context.BusinessPartners
            .AsNoTracking()
            .Where(x => x.QryGroup23 == "Y")
            .Select(bp => new WarehouseInfo
            {
                CardCode = bp.CardCode,
                CardFName = bp.CardFName,
                TaxId = bp.TaxId,
                Notes = bp.Notes,
                Address = bp.Addresses
                    .Where(a => a.AdresType == "S")
                    .OrderBy(a => a.AddressName)
                    .Select(a => new WarehouseAddress
                    {
                        City = a.City,
                        State = a.State
                    })
                    .FirstOrDefault()
            })
            .ToDictionaryAsync(x => x.CardCode);
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