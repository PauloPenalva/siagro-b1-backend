using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Shared.Base.Exceptions;

namespace SiagroB1.Domain.Shared.Base.Shared.Base
{
    public abstract class BaseService<T, ID>(DbContext context, ILogger<IBaseService<T, ID>> logger) : IBaseService<T, ID>
        where T : class
    {
        protected readonly DbContext _context = context;

        protected readonly ILogger<IBaseService<T, ID>> _logger = logger;

        public virtual async Task<T> CreateAsync(T entity)
        {
            try
            {
                await _context.Set<T>().AddAsync(entity);
                await _context.SaveChangesAsync();
                return entity;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating entity.");
                throw new DefaultException("Error creating entity.");
            }
        }

        public virtual async Task<bool> DeleteAsync(ID code)
        {
            try
            {
                var entity = await _context.Set<T>().FindAsync(code);
                if (entity == null)
                {
                    return false;
                }

                _context.Set<T>().Remove(entity);
                await _context.SaveChangesAsync();
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting entity.");
                throw new DefaultException("Error deleting entity.");
            }
        }

        public virtual IQueryable<T> QueryAll()
        {
            return _context.Set<T>().AsNoTracking();
        }

        public virtual async Task<IEnumerable<T>> GetAllAsync()
        {
            return await _context.Set<T>().ToListAsync();
        }

        public virtual async Task<T?> GetByIdAsync(ID code)
        {   
            try
            {
                _logger.LogInformation("Fetching entity with ID {Id}", code);
                return await _context.Set<T>().FindAsync(code);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,"Error fetching entity with ID {Id}", code);
                throw new DefaultException("Error fetching entity");
            }
        }

        public virtual async Task<T?> UpdateAsync(ID key, T entity)
        {
            try
            {
                _context.Entry(entity).State = EntityState.Modified;
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!EntityExists("Key", key))
                {
                    throw new KeyNotFoundException("Entity not found.");
                }
                else
                {
                    throw new DefaultException("Error updating entity due to concurrency issues.");
                }
            }

            return entity;
        }

        public virtual async Task<bool> DeleteAsyncWithTransaction(ID id, Func<T, Task>? preDeleteAction = null)
        {
            await using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                var entity = await _context.Set<T>().FindAsync(id);
                if (entity == null)
                {
                    _logger.LogWarning("Entity {Entity} with ID {Id} not found.", typeof(T).Name, id);
                    return false;
                }

                if (preDeleteAction != null)
                    await preDeleteAction(entity);

                _context.Set<T>().Remove(entity);
                await _context.SaveChangesAsync();

                await transaction.CommitAsync();
                return true;
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Error deleting entity {Entity} with ID {Id}", typeof(T).Name, id);
                throw;
            }
        }

        public virtual bool EntityExists(string property, ID key)
        {
#pragma warning disable CS8602 // Dereference of a possibly null reference.
            return _context.Set<T>().Any(e => EF.Property<ID>(e, property).Equals(key));
#pragma warning restore CS8602 // Dereference of a possibly null reference.
        }
        
        public virtual bool EntityExists<KT>(string property, KT key)
        {
#pragma warning disable CS8602 // Dereference of a possibly null reference.
            return _context.Set<T>().Any(e => EF.Property<KT>(e, property).Equals(key));
#pragma warning restore CS8602 // Dereference of a possibly null reference.
        }
        
    } 
}   