using SiagroB1.Core.Base;
using SiagroB1.Domain.Entities;

namespace SiagroB1.Core.Interfaces
{
    public interface ITabelaCustoQualidadeService : IBaseService<TabelaCustoQualidade, int>
    {
        Task<TabelaCustoQualidade> CreateAsync(int tabelaCustoId, TabelaCustoQualidade entity);
        Task<TabelaCustoQualidade?> FindByKeyAsync(int tabelaCustoId, int key);
        IQueryable<TabelaCustoQualidade> GetAllByTabelaCustoId(int tabelaCustoId);
    }
}