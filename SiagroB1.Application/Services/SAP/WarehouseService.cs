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
        // GAC-1196: armazém é parceiro (QryGroup23), então herda o Ativo/Inativo do OCRD.
        var inactiveCodes = context.BusinessPartners
            .Where(SapRecordActivity.IsInactive<Domain.Entities.SAP.BusinessPartner>(DateTime.Today))
            .Select(p => p.CardCode);

        return context.BusinessPartners
            .AsNoTracking()
            .Where(x => x.QryGroup23 == "Y")
            .Select(x => new WarehouseModel
            {
                Code = x.CardCode,
                Name = x.CardName,
                TaxId =  x.TaxId,
                FName = x.CardFName,
                Notes = x.Notes,
                Inactive = inactiveCodes.Contains(x.CardCode),
                // Primeiro endereço de ENTREGA pelo nome, como no LoadWarehousesAsync. Subconsulta
                // traduzível: a coluna Cidade/UF da tela de armazéns ordena e filtra por ela.
                City = x.Addresses
                    .Where(a => a.AdresType == "S")
                    .OrderBy(a => a.AddressName)
                    .Select(a => a.City)
                    .FirstOrDefault(),
                State = x.Addresses
                    .Where(a => a.AdresType == "S")
                    .OrderBy(a => a.AddressName)
                    .Select(a => a.State)
                    .FirstOrDefault(),
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
                CardName = bp.CardName,
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
            return await QueryAll().FirstOrDefaultAsync(x => x.Code == code);
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