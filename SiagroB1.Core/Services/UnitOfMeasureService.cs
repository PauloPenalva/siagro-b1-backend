using Microsoft.Extensions.Logging;
using SiagroB1.Core.Base;
using SiagroB1.Core.Interfaces;
using SiagroB1.Domain.Entities;
using SiagroB1.Infra.Context;

namespace SiagroB1.Core.Services
{
    public class UnitOfMeasureService(AppDbContext context, ILogger<UnitOfMeasureService> logger) 
        : BaseService<UnitOfMeasure, string>(context, logger), IUnitOfMeasureService
    {
        public override async Task<UnitOfMeasure> CreateAsync(UnitOfMeasure entity)
        {
            if (string.IsNullOrEmpty(entity.Key))
                throw new ArgumentException($"Chave não informada.");
            
            if (EntityExists(entity.Key))
                throw new ArgumentException($"Unidade de Medida com código '{entity.Key}' já existe.");

            try
            {
                await _context.Set<UnitOfMeasure>().AddAsync(entity);
                await _context.SaveChangesAsync();
                return entity;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating entity.");
                throw;
            }
        }

        private bool EntityExists(string key)
        {
            return context.UnitsOfMeasure.Any(x => x.Key == key);
        }
        
    }
}