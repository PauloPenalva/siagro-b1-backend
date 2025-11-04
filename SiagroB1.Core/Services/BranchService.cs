using Microsoft.EntityFrameworkCore;
using SiagroB1.Domain.Entities;
using SiagroB1.Infra.Context;
using SiagroB1.Domain.Exceptions;

namespace SiagroB1.Core.Services
{
    public class BranchService(AppDbContext context) : IBranchService
    {
        public async Task<Branch> CreateAsync(Branch entity)
        {
            var existingBranch = context.Branchs
                .FirstOrDefaultAsync(b => b.Key == entity.Key || b.BranchName == entity.BranchName);
            
            if (existingBranch != null)
            {
                throw new DefaultException($"Branch {entity.Key} already exists.");
            }
            
            await context.Branchs.AddAsync(entity);
            await context.SaveChangesAsync();
            return entity;
        }

        public async Task<bool> DeleteAsync(string key)
        {
            var entity = await context.Branchs.FindAsync(key);
            if (entity == null)
            {
                return false;
            }

            context.Branchs.Remove(entity);
            await context.SaveChangesAsync();
            return true;
        }

        public IQueryable<Branch> QueryAll()
        {
            return context.Branchs.AsNoTracking();
        }

        public async Task<IEnumerable<Branch>> GetAllAsync()
        {
            return await context.Branchs.ToListAsync();
        }

        public async Task<Branch?> GetByIdAsync(string key)
        {
            return await context.Branchs.FindAsync(key);
        }

        public async Task<Branch?> UpdateAsync(string key, Branch entity)
        {
            context.Entry(entity).State = EntityState.Modified;

            try
            {
                await context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!EntityExists(key))
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
        
        private bool EntityExists(string key)
        {
            return context.Branchs.Any(e => e.Key == key);
        }
    }
}