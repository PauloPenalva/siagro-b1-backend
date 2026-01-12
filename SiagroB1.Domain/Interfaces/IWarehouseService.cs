using SiagroB1.Domain.Models;

namespace SiagroB1.Domain.Interfaces;

public interface IWarehouseService 
{
    Task<IEnumerable<WarehouseModel>> GetAllAsync();
    Task<WarehouseModel?> GetByIdAsync(string code);
    Task<WarehouseModel> CreateAsync(WarehouseModel model);
    Task<WarehouseModel?> UpdateAsync(string code, WarehouseModel model);
    Task<bool> DeleteAsync(string code);
    IQueryable<WarehouseModel> QueryAll();
}