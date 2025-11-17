using SiagroB1.Domain.Entities;

namespace SiagroB1.Domain.Interfaces;
public interface IBranchService
{
    Task<IEnumerable<Branch>> GetAllAsync();
    Task<Branch?> GetByIdAsync(string key);
    Task<Branch> CreateAsync(Branch entity);
    Task<Branch?> UpdateAsync(string key, Branch entity);
    Task<bool> DeleteAsync(string key);
    IQueryable<Branch> QueryAll();
}
