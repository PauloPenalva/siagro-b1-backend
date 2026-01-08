using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using SiagroB1.Domain.Model;
using SiagroB1.Domain.Interfaces;
using SiagroB1.Domain.Shared.Base.Exceptions;
using SiagroB1.Infra.Context;

namespace SiagroB1.Application.Services.SAP;

public class AgentService(SapErpDbContext context, ILogger<AgentService> logger) 
    : IAgentService
{
     public Task<AgentModel> CreateAsync(AgentModel model)
     {
         throw new NotImplementedException("Not implemented on SAP context.");
     }

    public Task<bool> DeleteAsync(int code)
    {
        throw new NotImplementedException("Not implemented on SAP context.");
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
                Inactive =  x.Inactive ?? "N",
            })
            .ToListAsync();
    }

    public async Task<AgentModel?> GetByIdAsync(int code)
    {
        try
        {
            return await context.Agents
                .Select(x => new AgentModel
                {
                    Code = x.Code,
                    Name = x.Name,
                    Inactive =  x.Inactive ?? "N",
                })
                .FirstOrDefaultAsync(x => x.Code == code);
                
        }
        catch (Exception ex)
        {
            throw new DefaultException("Error fetching entity");
        }
    }
    
    public async Task<AgentModel?> UpdateAsync(int code, AgentModel model)
    {
        throw new NotImplementedException("Not implemented on SAP context.");
    }
    
}
