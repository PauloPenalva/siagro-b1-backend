using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Exceptions;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Domain.Models;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.SAP;

public class AgentService(SapErpDbContext context, ILogger<AgentService> logger) 
    : IAgentService
{
     public Task<AgentModel> CreateAsync(AgentModel model)
     {
         throw new NotImplementedException("Not implemented on SAP context.");
     }

     public Task<AgentModel?> UpdateAsync(int code, AgentModel model)
     {
         throw new NotImplementedException();
     }

     public Task<bool> DeleteAsync(int code)
    {
        throw new NotImplementedException("Not implemented on SAP context.");
    }

    public IQueryable<AgentModel> QueryAll()
    {
        return context.Agents
            .Select(x => new AgentModel()
            {
                Code = x.Code,
                Inactive = x.Inactive,
                Name = x.Name,
            })
            .AsNoTracking();
    }

    public async Task<IEnumerable<AgentModel>> GetAllAsync()
    {
        return await context.Agents
            .Select(x => new AgentModel()
            {
                Code = x.Code,
                Inactive = x.Inactive,
                Name = x.Name,
            })
           .ToListAsync();
    }

    public async Task<AgentModel?> GetByIdAsync(int code)
    {
        try
        {
            return await context.Agents
                .Select(x => new AgentModel()
                {
                    Code = x.Code,
                    Inactive = x.Inactive,
                    Name = x.Name,
                })
                .FirstOrDefaultAsync(x => x.Code == code);
                
        }
        catch (Exception ex)
        {
            throw new DefaultException("Error fetching entity");
        }
    }
    
    
}
