using SiagroB1.Core.Base;
using SiagroB1.Domain.Entities;

namespace SiagroB1.Core.Interfaces
{
    public interface IProcessingCostService : IBaseService<ProcessingCost, string>
    {
        Task<ProcessingCostServiceDetail?> FindTabelaCustoServicoById(string tabelaCustoId, string tabelaCustoServicoId);
        Task<ProcessingCostServiceDetail?> UpdateTabelaCustoServicoAsync(string id, ProcessingCostServiceDetail entity);
        IQueryable<ProcessingCostDryingParameter> GetDescontosSecagem(string id);
        IQueryable<ProcessingCostDryingDetail> GetValoresSecagem(string id);
        IQueryable<ProcessingCostServiceDetail> GetServicos(string id);
        IQueryable<ProcessingCostQualityParameter> GetQualidades(string id);
        Task<ProcessingCostDryingParameter> CreateDescontosSecagemAsync(string key, ProcessingCostDryingParameter t);
    }
}