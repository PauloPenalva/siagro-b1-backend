using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Domain.Shared.Base;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services;

public class TruckService(AppDbContext context, ILogger<TruckService> logger) :
    BaseService<Truck, string>(context, logger), ITruckService
{
    /// <summary>
    /// Grava a placa normalizada (ver <see cref="TruckPlateRules"/>) e recusa a que, normalizada,
    /// já existe — sem isso a duplicidade só aparecia como o erro genérico de chave do banco.
    /// </summary>
    public override async Task<Truck> CreateAsync(Truck entity)
    {
        entity.Code = TruckPlateRules.Normalize(entity.Code);

        if (await _context.Set<Truck>().AnyAsync(x => x.Code == entity.Code))
            throw new DefaultException($"Veículo {entity.Code} já cadastrado.");

        return await base.CreateAsync(entity);
    }

    /// <summary>
    /// A placa é a chave: o EF não deixa alterá-la e o PATCH estourava 500. Os demais campos
    /// continuam editáveis.
    /// </summary>
    public override async Task<Truck?> UpdateAsync(string key, Truck entity)
    {
        if (!string.Equals(entity.Code, key, StringComparison.OrdinalIgnoreCase))
            throw new DefaultException("A placa do veículo não pode ser alterada.");

        return await base.UpdateAsync(key, entity);
    }
}
