using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Domain.Models;
using SiagroB1.Domain.Shared.Base.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services;

public class AgentService(AppDbContext context, ILogger<AgentService> logger) : IAgentService
{
  public  async Task<AgentModel> CreateAsync(AgentModel model)
    {
        if (EntityExists(model.Code))
            throw new ArgumentException($"Comprador com código '{model.Code}' já existe.");

        try
        {
            var entity = new Agent
            {
                Code = model.Code,
                Name = model.Name,
                Inactive = model.Inactive,
            };
            
            await context.Agents.AddAsync(entity);
            await context.SaveChangesAsync();
            return model;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error creating model.");
            throw;
        }
    }
    
    public async Task<bool> DeleteAsync(int code)
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

    public IQueryable<AgentModel> QueryAll()
    {
        return context.Agents
            .AsNoTracking()
            .Select(x => new AgentModel
            {
                Code = x.Code,
                Name = x.Name,
                Inactive =  x.Inactive ?? "N",
            });
    }

    public async Task<IEnumerable<AgentModel>> GetAllAsync()
    {
        return await context.Agents
            .Select(x => new AgentModel
            {
                Code = x.Code,
                Name = x.Name,
                Inactive = x.Inactive ?? "N",
            })
            .ToListAsync();
    }

    public async Task<AgentModel?> GetByIdAsync(int code)
    {
        try
        {
            logger.LogInformation("Fetching entity with ID {Id}", code);

            return await context.Agents
                .Select(x => new AgentModel
                {
                    Code = x.Code,
                    Name = x.Name,
                    Inactive = x.Inactive ?? "N",
                })
                .FirstOrDefaultAsync(x => x.Code == code);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error fetching entity with ID {Id}", code);
            throw new DefaultException("Error fetching entity");
        }
    }


    public async Task<AgentModel?> UpdateAsync(int code, AgentModel model)
    {
        try
        {
            var entity = await context.Agents.FindAsync(code);
            if (entity == null) throw  new NotFoundException("Entity not found.");
            
            context.Entry(entity).State = EntityState.Modified;
            entity.Name = model.Name;
            entity.Inactive = model.Inactive;
            
            await context.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            if (!EntityExists(code))
            {
                throw new KeyNotFoundException("Entity not found.");
            }
            else
            {
                throw new DefaultException("Error updating model due to concurrency issues.");
            }
        }

        return model;
    }

        
    private bool EntityExists(int key)
    {
        return context.Agents.Any(x => x.Code == key);
    }
} 
