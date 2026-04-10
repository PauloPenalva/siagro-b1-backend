using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Domain.Models;
using SiagroB1.Infra;

namespace SiagroB1.Application.Services;

public class BusinessPartnerAddressService(
    IUnitOfWork db, 
    ILogger<BusinessPartnerAddressService> logger,
    IStringLocalizer<Resource> resource
    ) : IBusinessPartnerAddressService
{
    
    public IQueryable<AddressModel> QueryAll(string cardCode)
    {
        return db.Context.Addresses
            .Where(x => x.CardCode == cardCode)
            .Select(a => new AddressModel()
            {
                AddressName = a.AddressName,
                AdresType = a.AdresType,
                Block = a.Block,
                City = a.City,
                Country = a.Country,
                State = a.State,
                Street = a.Street,
                ZipCode = a.ZipCode,
            })
            .AsNoTracking();
    }
    
    public async Task<AddressModel?> GetByIdAsync(string cardCode, string addressName, string adresType)
    {
        var entity = await db.Context.Addresses
                         .FirstOrDefaultAsync(x => x.CardCode == cardCode &&
                                                   x.AddressName == addressName && 
                                                   x.AdresType == adresType)
                     ?? throw new NotFoundException(resource["BP_NOT_FOUND"]);

        return new AddressModel()
        {
            AddressName = entity.AddressName,
            AdresType = entity.AdresType,
            Block = entity.Block,
            City = entity.City,
            Country = entity.Country,
            State = entity.State,
            Street = entity.Street,
            ZipCode = entity.ZipCode,
        };
    }
    
    public async Task<AddressModel> Create(string cardCode, AddressModel addressModel)
    {
        var existingEntity = await db.Context.BusinessPartners.FindAsync(cardCode)
                             ?? throw new NotFoundException(resource["BP_NOT_FOUND"]);

        var address = new Address()
        {
            AddressName = addressModel.AddressName,
            AdresType = addressModel.AdresType,
            Block = addressModel.Block,
            BusinessPartner = existingEntity,
            CardCode = cardCode,
            City = addressModel.City,
            Country = addressModel.Country,
            State = addressModel.State,
            Street = addressModel.Street,
            ZipCode = addressModel.ZipCode,
        };
        
        try
        {
            await db.Context.AddAsync(address);
            await db.SaveChangesAsync();
            
            return addressModel;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, exception.Message);
            throw;
        }
    }
    
    public async Task<AddressModel> Update(string cardCode, string addressName, string adresType, AddressModel addressModel)
    {
        var entity = await db.Context.Addresses
                .FirstOrDefaultAsync(x => x.CardCode == cardCode &&
                                          x.AddressName == addressName && 
                                          x.AdresType == adresType)
                             ?? throw new NotFoundException(resource["BP_NOT_FOUND"]);
        
        db.Context.Entry(entity).State = EntityState.Modified;
        
        entity.Block = addressModel.Block;
        entity.City = addressModel.City;
        entity.Country = addressModel.Country;
        entity.State = addressModel.State;
        entity.Street = addressModel.Street;
        entity.ZipCode = addressModel.ZipCode;
        
        try
        {
            await db.SaveChangesAsync();
            
            return addressModel;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, exception.Message);
            throw;
        }
    }
    
    public async Task<bool> Delete(string cardCode, string addressName, string adresType)
    {
        try
        {
            var entity = await db.Context.Addresses
                             .FirstOrDefaultAsync(x => x.CardCode == cardCode &&
                                                       x.AddressName == addressName && 
                                                       x.AdresType == adresType)
                         ?? throw new NotFoundException(resource["BP_NOT_FOUND"]);

            db.Context.Addresses.Remove(entity);
            await db.Context.SaveChangesAsync();
            
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, ex.Message);
            throw;
        }
    }
}