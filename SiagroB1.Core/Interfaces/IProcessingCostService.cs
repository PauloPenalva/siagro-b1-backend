using SiagroB1.Core.Base;
using SiagroB1.Domain.Entities;

namespace SiagroB1.Core.Interfaces
{
    public interface IProcessingCostService : IBaseService<ProcessingCost, Guid>
    {
        Task<ProcessingCostServiceDetail?> FindTabelaCustoServicoById(Guid tabelaCustoId, Guid tabelaCustoServicoId);
        Task<ProcessingCostServiceDetail?> UpdateTabelaCustoServicoAsync(Guid id, ProcessingCostServiceDetail entity);
        IQueryable<ProcessingCostDryingParameter> GetDescontosSecagem(Guid id);
        IQueryable<ProcessingCostDryingDetail> GetValoresSecagem(Guid id);
        IQueryable<ProcessingCostServiceDetail> GetServicos(Guid id);
        IQueryable<ProcessingCostQualityParameter> GetQualidades(Guid id);
        Task<ProcessingCostDryingParameter> CreateDescontosSecagemAsync(Guid key, ProcessingCostDryingParameter t);
    }
}