using SiagroB1.Domain.Models;

namespace SiagroB1.Domain.Interfaces;

public interface IUnitOfMeasureService
{
    Task<IEnumerable<UnitOfMeasureModel>> GetAllAsync();
    Task<UnitOfMeasureModel?> GetByIdAsync(string code);
    Task<UnitOfMeasureModel> CreateAsync(UnitOfMeasureModel model);
    Task<UnitOfMeasureModel?> UpdateAsync(string key, UnitOfMeasureModel model);
    Task<bool> DeleteAsync(string code);
    IQueryable<UnitOfMeasureModel> QueryAll();
}
