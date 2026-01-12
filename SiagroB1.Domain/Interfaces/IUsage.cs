using SiagroB1.Domain.Models;

namespace SiagroB1.Domain.Interfaces;

public interface IUsage
{
    Task<IEnumerable<UsageModel>> GetAllAsync();
    Task<UsageModel?> GetByIdAsync(int key);
    Task<UsageModel> CreateAsync(UsageModel model);
    Task<UsageModel?> UpdateAsync(int key, UsageModel model);
    Task<bool> DeleteAsync(int key);
    IQueryable<UsageModel> QueryAll();
}