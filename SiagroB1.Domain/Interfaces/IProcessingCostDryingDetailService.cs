
using SiagroB1.Domain.Entities;
using SiagroB1.Domain.Shared.Base;

namespace SiagroB1.Domain.Interfaces;

public interface IProcessingCostDryingDetailService : IBaseService<ProcessingCostDryingDetail, int>
{
    Task<ProcessingCostDryingDetail> CreateAsync(string processingCostCode, ProcessingCostDryingDetail entity);
    Task<ProcessingCostDryingDetail?> FindByKeyAsync(string processingCostCode, int itemId);
    IQueryable<ProcessingCostDryingDetail> GetAllByProcessingCostKey(string processingCostCode);
}
