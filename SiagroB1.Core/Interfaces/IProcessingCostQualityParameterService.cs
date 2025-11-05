using SiagroB1.Core.Base;
using SiagroB1.Domain.Entities;

namespace SiagroB1.Core.Interfaces
{
    public interface IProcessingCostQualityParameterService : IBaseService<ProcessingCostQualityParameter, int>
    {
        Task<ProcessingCostQualityParameter> CreateAsync(string processingCostKey, ProcessingCostQualityParameter entity);
        Task<ProcessingCostQualityParameter?> FindByKeyAsync(string processingCostKey, int itemId);
        IQueryable<ProcessingCostQualityParameter> GetAllByProcessingCostKey(string processingCostKey);
    }
}