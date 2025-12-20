using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Shared.Base;

namespace SiagroB1.Domain.Interfaces;

public interface IProcessingCostDryingParameterService : IBaseService<ProcessingCostDryingParameter, int>
{
    Task<ProcessingCostDryingParameter> CreateAsync(string processingCostCode, ProcessingCostDryingParameter entity);
    Task<ProcessingCostDryingParameter?> FindByKeyAsync(string processingCostCode, int itemId);
    IQueryable<ProcessingCostDryingParameter> GetAllByTabelaCustoId(string processingCostCode);
}
