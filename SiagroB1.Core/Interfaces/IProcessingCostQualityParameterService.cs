using SiagroB1.Core.Base;
using SiagroB1.Domain.Entities;

namespace SiagroB1.Core.Interfaces
{
    public interface IProcessingCostQualityParameterService : IBaseService<ProcessingCostQualityParameter, Guid>
    {
        Task<ProcessingCostQualityParameter> CreateAsync(Guid processingCostKey, ProcessingCostQualityParameter entity);
        Task<ProcessingCostQualityParameter?> FindByKeyAsync(Guid processingCostKey, Guid key);
        IQueryable<ProcessingCostQualityParameter> GetAllByProcessingCostKey(Guid processingCostKey);
    }
}