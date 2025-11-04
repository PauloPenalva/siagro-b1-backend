using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Core.Base;
using SiagroB1.Domain.Entities;
using SiagroB1.Infra.Context;

namespace SiagroB1.Core.Services
{
    public class UnidadeMedidaService(AppDbContext context, ILogger<UnidadeMedidaService> logger) : BaseService<UnidadeMedida, string>(context, logger), IUnidadeMedidaService
    {
        public override async Task<UnidadeMedida> CreateAsync(UnidadeMedida entity)
        {
            if (EntityExists("Id", entity.Id))
                throw new ArgumentException($"Unidade de Medida com código '{entity.Id}' já existe.");

            try
            {
                await _context.Set<UnidadeMedida>().AddAsync(entity);
                await _context.SaveChangesAsync();
                return entity;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating entity.");
                throw;
            }
        }

        public override bool EntityExists(string property, string key)
        {
            return _context.Set<UnidadeMedida>().Any(e => EF.Property<string>(e, property) == key);
        }
        
    }
}