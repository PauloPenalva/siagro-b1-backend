using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using SiagroB1.Commons.Resources;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Enums;
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
                    TaxId = x.TaxId,
                    StateRegistration = x.StateRegistration,
                    StateRegistrationIndicator = x.StateRegistrationIndicator,
                    NfeEmail = x.NfeEmail,
                    Phone = x.Phone,
                    PaymentConditionCode = x.PaymentConditionCode,
                    // O Include acima é decorativo sem esta projeção: o EF materializa o
                    // Model, não a entidade, e a coleção voltava SEMPRE vazia. Quem depende
                    // dela é a resolução de CFOP (UF do destinatário), que rejeitaria todo
                    // documento de saída em modo STANDALONE. O equivalente em SAPB1 já
                    // projeta o endereço.
                    Addresses = x.Addresses
                        .Select(a => new AddressModel()
                        {
                            CardCode = a.CardCode,
                            AddressName = a.AddressName,
                            AdresType = a.AdresType,
                            Block = a.Block,
                            City = a.City,
                            Country = a.Country,
                            State = a.State,
                            Street = a.Street,
                            ZipCode = a.ZipCode,
                            StreetNumber = a.StreetNumber,
                            Complement = a.Complement,
                            MunicipalityCode = a.MunicipalityCode,
                        })
                        .ToList()
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

        ValidateNfeFields(model);

        var entity = new BusinessPartner()
        {
            CardCode = model.CardCode,
            CardName = model.CardName,
            CardFName = model.CardFName,
            CardType = model.CardType,
            QryGroup23 = "N",
            TaxId = model.TaxId,
            StateRegistration = model.StateRegistration,
            StateRegistrationIndicator = model.StateRegistrationIndicator,
            NfeEmail = model.NfeEmail,
            Phone = model.Phone,
            PaymentConditionCode = model.PaymentConditionCode,
        };

        // A tela cria o parceiro com os endereços aninhados (deep insert). Sem copiá-los o
        // parceiro nascia sem endereço, e é do endereço de faturamento que sai a UF do CFOP e
        // do cálculo de tributos do documento de saída.
        foreach (var address in model.Addresses)
        {
            var entityAddress = new Address
            {
                CardCode = model.CardCode,
                AddressName = address.AddressName,
                AdresType = address.AdresType,
                Street = address.Street,
                StreetNumber = address.StreetNumber,
                Complement = address.Complement,
                Block = address.Block,
                ZipCode = address.ZipCode,
                City = address.City,
                State = address.State,
                Country = address.Country,
                MunicipalityCode = address.MunicipalityCode,
            };

            await AddressMunicipalityResolver.ApplyAsync(db.Context, entityAddress);
            entity.Addresses.Add(entityAddress);
        }

        await db.Context.BusinessPartners.AddAsync(entity);
        await db.SaveChangesAsync();
        return model;
    }

    public async Task<BusinessPartnerModel?> UpdateAsync(string code, BusinessPartnerModel model)
    {
        ValidateNfeFields(model);

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
        entity.StateRegistration = model.StateRegistration;
        entity.StateRegistrationIndicator = model.StateRegistrationIndicator;
        entity.NfeEmail = model.NfeEmail;
        entity.Phone = model.Phone;
        entity.PaymentConditionCode = model.PaymentConditionCode;

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
                StateRegistration = x.StateRegistration,
                StateRegistrationIndicator = x.StateRegistrationIndicator,
                NfeEmail = x.NfeEmail,
                Phone = x.Phone,
                PaymentConditionCode = x.PaymentConditionCode,
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
                        ZipCode = a.ZipCode,
                        StreetNumber = a.StreetNumber,
                        Complement = a.Complement,
                        MunicipalityCode = a.MunicipalityCode,
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

        return await db.Context.BusinessPartners
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
    
    /// <summary>
    /// Coerência dos campos da NF-e. O serviço local só existe em STANDALONE, então a regra já
    /// nasce restrita ao modo. Contribuinte (indicador 1) exige IE só com dígitos — é o tipo do
    /// schema para <c>dest/IE</c>; isento e não contribuinte não levam IE no XML.
    /// </summary>
    private static void ValidateNfeFields(BusinessPartnerModel model)
    {
        if (model.StateRegistrationIndicator == StateRegistrationIndicator.Taxpayer)
        {
            if (string.IsNullOrWhiteSpace(model.StateRegistration))
                throw new DefaultException("Parceiro contribuinte do ICMS precisa da inscrição estadual.");

            if (!System.Text.RegularExpressions.Regex.IsMatch(model.StateRegistration.Trim(), "^[0-9]{2,14}$"))
                throw new DefaultException("A inscrição estadual do contribuinte deve ter só dígitos (2 a 14).");
        }

        if (!string.IsNullOrWhiteSpace(model.NfeEmail) &&
            (!model.NfeEmail.Contains('@') || model.NfeEmail.Contains(' ')))
            throw new DefaultException($"E-mail da NF-e inválido: {model.NfeEmail}.");
    }

    private bool EntityExists(string code)
    {
        return db.Context.BusinessPartners.Any(e => e.CardCode == code);
    }
}