using SiagroB1.Core.Base;
using SiagroB1.Domain.Entities;

namespace SiagroB1.Core.Interfaces
{
    public interface ITabelaCustoValorSecagemService : IBaseService<TabelaCustoValorSecagem, int>
    {
        Task<TabelaCustoValorSecagem> CreateAsync(int tabelaCustoId, TabelaCustoValorSecagem entity);
        Task<TabelaCustoValorSecagem?> FindByKeyAsync(int tabelaCustoId, int key);
        IQueryable<TabelaCustoValorSecagem> GetAllByTabelaCustoId(int tabelaCustoId);
    }
}