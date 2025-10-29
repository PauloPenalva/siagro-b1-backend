using SiagroB1.Core.Base;
using SiagroB1.Domain.Entities;

namespace SiagroB1.Core.Interfaces
{
    public interface ITabelaCustoService : IBaseService<TabelaCusto, int>
    {
        Task<TabelaCustoServico?> FindTabelaCustoServicoById(int tabelaCustoId, int tabelaCustoServicoId);
        Task<TabelaCustoServico?> UpdateTabelaCustoServicoAsync(int id, TabelaCustoServico entity);
        IQueryable<TabelaCustoDescontoSecagem> GetDescontosSecagem(int id);
        IQueryable<TabelaCustoValorSecagem> GetValoresSecagem(int id);
        IQueryable<TabelaCustoServico> GetServicos(int id);
        IQueryable<TabelaCustoQualidade> GetQualidades(int id);
        Task<TabelaCustoDescontoSecagem> CreateDescontosSecagemAsync(int key, TabelaCustoDescontoSecagem t);
    }
}