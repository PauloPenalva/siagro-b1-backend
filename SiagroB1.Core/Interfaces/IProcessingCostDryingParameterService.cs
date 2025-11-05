using SiagroB1.Core.Base;
using SiagroB1.Domain.Entities;

namespace SiagroB1.Core.Interfaces
{
    public interface IProcessingCostDryingParameterService : IBaseService<ProcessingCostDryingParameter, int>
    {
        Task<ProcessingCostDryingParameter> CreateAsync(string processingCostKey, ProcessingCostDryingParameter entity);
        Task<ProcessingCostDryingParameter?> FindByKeyAsync(string processingCostKey, int itemId);
        IQueryable<ProcessingCostDryingParameter> GetAllByTabelaCustoId(string processingCostKey);
    }
}