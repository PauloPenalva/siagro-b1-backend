using SiagroB1.Domain.Entities;

namespace SiagroB1.Core.Services
{
    public interface IFilialService
    {
        Task<IEnumerable<Filial>> GetAllAsync();
        Task<Filial?> GetByIdAsync(int id);
        Task<Filial> CreateAsync(Filial entity);
        Task<Filial?> UpdateAsync(int id, Filial entity);
        Task<bool> DeleteAsync(int id);
        IQueryable<Filial> QueryAll();
    }
}