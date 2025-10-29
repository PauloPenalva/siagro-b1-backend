using SiagroB1.Core.Base;
using SiagroB1.Domain.Entities;
// ReSharper disable All

namespace SiagroB1.Core.Interfaces
{
    public interface ITabelaCustoServicoService : IBaseService<TabelaCustoServico, int>
    {
        Task<TabelaCustoServico> CreateAsync(int tabelaCustoId, TabelaCustoServico entity);
        Task<TabelaCustoServico?> FindByKeyAsync(int tabelaCustoId, int key);
        IQueryable<TabelaCustoServico> GetAllByTabelaCustoId(int tabelaCustoId);
    }
}