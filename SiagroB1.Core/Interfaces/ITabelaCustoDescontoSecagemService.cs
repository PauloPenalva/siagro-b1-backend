using SiagroB1.Core.Base;
using SiagroB1.Domain.Entities;

namespace SiagroB1.Core.Interfaces
{
    public interface ITabelaCustoDescontoSecagemService : IBaseService<TabelaCustoDescontoSecagem, int>
    {
        Task<TabelaCustoDescontoSecagem> CreateAsync(int tabelaCustoId, TabelaCustoDescontoSecagem entity);
        Task<TabelaCustoDescontoSecagem?> FindByKeyAsync(int tabelaCustoId, int key);
        IQueryable<TabelaCustoDescontoSecagem> GetAllByTabelaCustoId(int tabelaCustoId);
    }
}