using SiagroB1.Core.Base;
using SiagroB1.Domain.Entities;

namespace SiagroB1.Core.Interfaces
{
    public interface IProcessingCostDryingParameterService : IBaseService<ProcessingCostDryingParameter, string>
    {
        Task<ProcessingCostDryingParameter> CreateAsync(string processingCostKey, ProcessingCostDryingParameter entity);
        Task<ProcessingCostDryingParameter?> FindByKeyAsync(string field, string processingCostKey);
        IQueryable<ProcessingCostDryingParameter> GetAllByTabelaCustoId(string processingCostKey);
    }
}