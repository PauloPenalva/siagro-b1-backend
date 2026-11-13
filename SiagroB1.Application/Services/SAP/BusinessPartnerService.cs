using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Domain.Models;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.SAP;

public class BusinessPartnerService(SapErpDbContext context, ILogger<BusinessPartnerService> logger) 
    : IBusinessPartnerService
{
    public Task<IEnumerable<BusinessPartnerModel>> GetAllAsync()
    {
        throw new NotImplementedException();
    }

    public async Task<BusinessPartnerModel?> GetByIdAsync(string code)
    {
        try
        {
            logger.LogInformation("Fetching entity with ID {Id}", code);

            // Sem filtro de inativo de propósito (GAC-1196): documento antigo precisa resolver o
            // parceiro que foi inativado depois. Quem ESCOLHE o parceiro confere `Inactive`.
            return await QueryAll().FirstOrDefaultAsync(x => x.CardCode == code);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error fetching entity with ID {Id}", code);
            throw new DefaultException("Error fetching entity");
        }
    }

    public Task<BusinessPartnerModel> CreateAsync(BusinessPartnerModel entity)
    {
        throw new NotImplementedException();
    }

    public Task<BusinessPartnerModel?> UpdateAsync(string code, BusinessPartnerModel entity)
    {
        throw new NotImplementedException();
    }

    public Task<bool> DeleteAsync(string code)
    {
        throw new NotImplementedException();
    }

    public IQueryable<BusinessPartnerModel> QueryAll()
    {
        // GAC-1196: subconsulta em vez de repetir a regra na projeção — o $filter do value help
        // (Inactive eq false) vira um NOT EXISTS pela chave.
        var inactiveCodes = context.BusinessPartners
            .Where(SapRecordActivity.IsInactive<Domain.Entities.SAP.BusinessPartner>(DateTime.Today))
            .Select(p => p.CardCode);

        return context.BusinessPartners
            .Include(a => a.Addresses)
            .Select(x => new BusinessPartnerModel()
            {
                CardCode = x.CardCode,
                CardName = x.CardName,
                CardFName = x.CardFName,
                CardType = x.CardType,
                Notes = x.Notes,
                QryGroup23 = x.QryGroup23,
                TaxId = x.TaxId,
                Inactive = inactiveCodes.Contains(x.CardCode),
                Addresses = x.Addresses
                    .Where(a => a.CardCode == x.CardCode && a.AddressName == "FATURAMENTO")
                    .Take(1)
                    .OrderBy(a => a.AddressName)
                    .Select(a => new AddressModel()
                    {
                        CardCode =  a.CardCode,
                        AddressName = a.AddressName,
                        AdresType = a.AdresType,
                        Block = a.Block,
                        City = a.City,
                        Country = a.Country,
                        State = a.State,
                        Street = a.Street,
                        ZipCode = a.ZipCode
                    })
                    .AsQueryable()
                    .ToList()
            })
            .AsNoTracking();
    }

    public Task<bool> DeleteAsyncWithTransaction(string code, Func<BusinessPartnerModel, Task>? preDeleteAction = null)
    {
        throw new NotImplementedException();
    }

    public async Task<Dictionary<string, SupplierInfo>> LoadSuppliersAsync(IReadOnlyCollection<string> cardCodes)
    {
        if (cardCodes.Count == 0)
        {
            return new Dictionary<string, SupplierInfo>();
        }

        return await context.BusinessPartners
            .AsNoTracking()
            .Where(bp => cardCodes.Contains(bp.CardCode))
            .Select(bp => new SupplierInfo
            {
                CardCode = bp.CardCode,
                CardFName = bp.CardFName,
                CardName = bp.CardName,
                TaxId = bp.TaxId,
                Notes = bp.Notes,
                Address = bp.Addresses
                    .Where(a => a.AdresType == "S")
                    .OrderBy(a => a.AddressName)
                    .Select(a => new SupplierAddress
                    {
                        City = a.City,
                        State = a.State
                    })
                    .FirstOrDefault()
            })
            .ToDictionaryAsync(x => x.CardCode);
    }
}