using SiagroB1.Domain.Model;

namespace SiagroB1.Domain.Interfaces;

public interface IAgentService
{
    Task<IEnumerable<AgentModel>> GetAllAsync();
    Task<AgentModel?> GetByIdAsync(int code);
    Task<AgentModel> CreateAsync(AgentModel model);
    Task<AgentModel?> UpdateAsync(int code, AgentModel model);
    Task<bool> DeleteAsync(int code);
    IQueryable<AgentModel> QueryAll();
}