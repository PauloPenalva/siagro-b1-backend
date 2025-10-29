using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Infra.Context;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Core.Services
{
    public class FilialService(AppDbContext context) : IFilialService
    {
        private readonly AppDbContext _context = context;

        public async Task<Filial> CreateAsync(Filial entity)
        {
            await _context.Filiais.AddAsync(entity);
            await _context.SaveChangesAsync();
            return entity;
        }

        public async Task<bool> DeleteAsync(int id)
        {
            var entity = await _context.Filiais.FindAsync(id);
            if (entity == null)
            {
                return false;
            }

            _context.Filiais.Remove(entity);
            await _context.SaveChangesAsync();
            return true;
        }

        public IQueryable<Filial> QueryAll()
        {
            return _context.Filiais.AsNoTracking();
        }

        public async Task<IEnumerable<Filial>> GetAllAsync()
        {
            return await _context.Filiais.ToListAsync();
        }

        public async Task<Filial?> GetByIdAsync(int id)
        {
            return await _context.Filiais.FindAsync(id);
        }

        public async Task<Filial?> UpdateAsync(int id, Filial entity)
        {
            _context.Entry(entity).State = EntityState.Modified;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!EntityExists(id))
                {
                    throw new KeyNotFoundException("Entity not found.");
                }
                else
                {
                    throw new DefaultException("Error updating entity.");
                }
            }

            return entity;
        }
        
        private bool EntityExists(int key)
        {
            return _context.Filiais.Any(e => e.Id == key);
        }
    }
}