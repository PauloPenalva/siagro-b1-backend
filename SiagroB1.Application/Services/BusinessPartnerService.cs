using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Domain.Models;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services;

public class BusinessPartnerService(
    IUnitOfWork db, 
    ILogger<BusinessPartnerService> logger,
    IStringLocalizer<Resource> resource
    ) 
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
            return await db.Context.BusinessPartners
                .Include(a => a.Addresses)
                .Select(x => new BusinessPartnerModel()
                {
                    CardCode = x.CardCode,
                    CardName = x.CardName,
                    CardFName = x.CardFName,
                    CardType = x.CardType,
                    Notes = x.Notes,
                    QryGroup23 = x.QryGroup23,
                    TaxId = x.TaxId
                })
                .FirstOrDefaultAsync(x => x.CardCode == code);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error fetching entity with ID {Id}", code);
            throw new DefaultException(ex.Message);
        }
    }

    public async Task<BusinessPartnerModel> CreateAsync(BusinessPartnerModel model)
    {
        var existingCode = await db.Context.BusinessPartners
            .FirstOrDefaultAsync(b => b.CardCode == model.CardCode);
        
        if (existingCode != null)
        {
            throw new DefaultException(resource["BP_EXISTING_CODE"]);
        }

        var entity = new BusinessPartner()
        {
            CardCode = model.CardCode,
            CardName = model.CardName,
            CardFName = model.CardFName,
            CardType = model.CardType,
            QryGroup23 = "N",
            TaxId = model.TaxId
        };
        
        await db.Context.BusinessPartners.AddAsync(entity);
        await db.SaveChangesAsync();
        return model;
    }

    public async Task<BusinessPartnerModel?> UpdateAsync(string code, BusinessPartnerModel model)
    {
        var entity = await db.Context.BusinessPartners
            .Include(a => a.Addresses)
            .FirstOrDefaultAsync(x => x.CardCode == model.CardCode);

        if (entity == null)
            throw new NotFoundException(resource["BP_NOT_FOUND"]);
        
        db.Context.Entry(entity).State = EntityState.Modified;

        entity.CardName = model.CardName;
        entity.CardFName = model.CardFName;
        entity.CardType = model.CardType;
        entity.Notes = model.Notes;
        entity.TaxId = model.TaxId;

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException e)
        {
           throw new DefaultException(e.Message);
        }

        return model;
    }

    public async Task<bool> DeleteAsync(string code)
    {
        var entity = await db.Context.BusinessPartners.FindAsync(code);
        if (entity == null)
        {
            return false;
        }

        db.Context.BusinessPartners.Remove(entity);
        await db.SaveChangesAsync();
        return true;
    }

    public IQueryable<BusinessPartnerModel> QueryAll()
    {
        return db.Context.BusinessPartners
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
                Addresses = x.Addresses
                    .Where(a => a.CardCode == x.CardCode)
                    .Select(a => new AddressModel()
                    {
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

    public async Task<Dictionary<string, SupplierInfo>> LoadSuppliersAsync()
    {
        return await db.Context.BusinessPartners
            .AsNoTracking()
            .Where(x => x.QryGroup23 == "Y")
            .Select(bp => new SupplierInfo
            {
                CardCode = bp.CardCode,
                CardFName = bp.CardFName,
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
    
    private bool EntityExists(string code)
    {
        return db.Context.BusinessPartners.Any(e => e.CardCode == code);
    }
}