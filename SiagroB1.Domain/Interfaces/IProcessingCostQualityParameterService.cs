using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Shared.Base;

namespace SiagroB1.Domain.Interfaces;

public interface IProcessingCostQualityParameterService : IBaseService<ProcessingCostQualityParameter, int>
{
    Task<ProcessingCostQualityParameter> CreateAsync(string processingCostCode, ProcessingCostQualityParameter entity);
    Task<ProcessingCostQualityParameter?> FindByKeyAsync(string processingCostKey, int itemId);
    IQueryable<ProcessingCostQualityParameter> GetAllByProcessingCostKey(string processingCostCode);
}
