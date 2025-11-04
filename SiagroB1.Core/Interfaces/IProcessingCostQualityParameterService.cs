using SiagroB1.Core.Base;
using SiagroB1.Domain.Entities;

namespace SiagroB1.Core.Interfaces
{
    public interface IProcessingCostQualityParameterService : IBaseService<ProcessingCostQualityParameter, string>
    {
        Task<ProcessingCostQualityParameter> CreateAsync(string processingCostKey, ProcessingCostQualityParameter entity);
        Task<ProcessingCostQualityParameter?> FindByKeyAsync(string field, string key);
        IQueryable<ProcessingCostQualityParameter> GetAllByProcessingCostKey(string processingCostKey);
    }
}