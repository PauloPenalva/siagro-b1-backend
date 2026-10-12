using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Dtos;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Domain.Models;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services;

/// <summary>
/// Armazém em STANDALONE: o parceiro de negócio com <c>QryGroup23 = 'Y'</c>, espelhando
/// <see cref="SAP.WarehouseService"/>. A tabela <c>WAREHOUSES</c> não serve para NF-e (sem IE nem
/// endereço) e ficou sem uso; o cadastro é feito no Parceiro de Negócio.
/// </summary>
public class WarehouseService(AppDbContext context, ILogger<WarehouseService> logger)
    : IWarehouseService
{
    public const string ManagedByBusinessPartnerMessage =
        "Cadastre o armazém no Parceiro de Negócio, marcando 'Armazém'.";

    public Task<WarehouseModel> CreateAsync(WarehouseModel model) =>
        throw new DefaultException(ManagedByBusinessPartnerMessage);

    public Task<WarehouseModel?> UpdateAsync(string code, WarehouseModel model) =>
        throw new DefaultException(ManagedByBusinessPartnerMessage);

    public Task<bool> DeleteAsync(string code) =>
        throw new DefaultException(ManagedByBusinessPartnerMessage);

    public IQueryable<WarehouseModel> QueryAll()
    {
        return context.BusinessPartners
            .AsNoTracking()
            .Where(x => x.QryGroup23 == "Y")
            .Select(x => new WarehouseModel
            {
                Code = x.CardCode,
                Name = x.CardName,
                FName = x.CardFName,
                TaxId = x.TaxId,
                Notes = x.Notes,
                // Mesma regra do LoadWarehousesAsync: primeiro endereço de ENTREGA pelo nome.
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

    public async Task<IEnumerable<WarehouseModel>> GetAllAsync() =>
        await QueryAll().ToListAsync();

    public async Task<WarehouseModel?> GetByIdAsync(string code)
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
}
