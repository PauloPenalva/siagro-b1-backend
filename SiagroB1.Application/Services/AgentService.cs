using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Shared.Base.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services;

public class AgentService(AppDbContext context, ILogger<AgentService> logger) 
{
  public virtual async Task<Agent> CreateAsync(Agent entity)
    {
        try
        {
            await context.Agents.AddAsync(entity);
            await context.SaveChangesAsync();
            return entity;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error creating entity.");
            throw new DefaultException("Error creating entity.");
        }
    }

    public virtual async Task<bool> DeleteAsync(string code)
    {
        try
        {
            var entity = await context.Agents.FindAsync(code);
            if (entity == null)
            {
                return false;
            }

            context.Agents.Remove(entity);
            await context.SaveChangesAsync();
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error deleting entity.");
            throw new DefaultException("Error deleting entity.");
        }
    }

    public virtual IQueryable<Agent> QueryAll()
    {
        return context.Agents.AsNoTracking();
    }

    public virtual async Task<IEnumerable<Agent>> GetAllAsync()
    {
        return await context.Agents.ToListAsync();
    }

    public virtual async Task<Agent?> GetByIdAsync(string code)
    {   
        try
        {
            logger.LogInformation("Fetching entity with ID {Id}", code);
            return await context.Agents.FindAsync(code);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,"Error fetching entity with ID {Id}", code);
            throw new DefaultException("Error fetching entity");
        }
    }

    public virtual async Task<Agent?> UpdateAsync(string key, Agent entity)
    {
        try
        {
            context.Entry(entity).State = EntityState.Modified;
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
                throw new DefaultException("Error updating entity due to concurrency issues.");
            }
        }

        return entity;
    }

    public virtual async Task<bool> DeleteAsyncWithTransaction(string id, Func<Agent, Task>? preDeleteAction = null)
    {
        await using var transaction = await context.Database.BeginTransactionAsync();
        try
        {
            var entity = await context.Agents.FindAsync(id);
            if (entity == null)
            {
                logger.LogWarning("Entity {Entity} with ID {Id} not found.", typeof(Agent).Name, id);
                return false;
            }

            if (preDeleteAction != null)
                await preDeleteAction(entity);

            context.Agents.Remove(entity);
            await context.SaveChangesAsync();

            await transaction.CommitAsync();
            return true;
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            logger.LogError(ex, "Error deleting entity {Entity} with ID {Id}", nameof(Agent), id);
            throw;
        }
    }

    private bool EntityExists(string key) => context.Agents.Any(e => e.Code == key);
} 
