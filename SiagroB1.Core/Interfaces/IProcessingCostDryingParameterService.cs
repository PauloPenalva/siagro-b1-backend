using SiagroB1.Core.Base;
using SiagroB1.Domain.Entities;

namespace SiagroB1.Core.Interfaces
{
    public interface IProcessingCostDryingParameterService : IBaseService<ProcessingCostDryingParameter, Guid>
    {
        Task<ProcessingCostDryingParameter> CreateAsync(Guid processingCostKey, ProcessingCostDryingParameter entity);
        Task<ProcessingCostDryingParameter?> FindByKeyAsync(Guid processingCostKey, Guid itemId);
        IQueryable<ProcessingCostDryingParameter> GetAllByTabelaCustoId(Guid processingCostKey);
    }
}